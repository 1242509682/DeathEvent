using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using static DeathEvent.Data;

namespace DeathEvent;

[ApiVersion(2, 1)]
public class DeathEvent : TerrariaPlugin
{
    #region 插件信息
    public override string Name => "共同死亡事件";
    public override string Author => "Kimi,羽学";
    public override Version Version => new(1, 0, 4);
    public override string Description => "玩家死亡实现共同死亡事件,允许重生后实现补偿,支持队伍模式";
    #endregion

    #region 注册与释放
    public DeathEvent(Main game) : base(game) { }
    public override void Initialize()
    {
        LoadConfig();
        GeneralHooks.ReloadEvent += ReloadConfig;
        GetDataHandlers.PlayerSpawn += OnSpawn!;
        GetDataHandlers.KillMe += OnKillMe;
        GetDataHandlers.PlayerTeam += OnPlayerTeam;
        ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GeneralHooks.ReloadEvent -= ReloadConfig;
            GetDataHandlers.PlayerSpawn -= OnSpawn!;
            GetDataHandlers.KillMe -= OnKillMe;
            GetDataHandlers.PlayerTeam -= OnPlayerTeam;
            ServerApi.Hooks.ServerLeave.Deregister(this, OnServerLeave);
            ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreetPlayer);
        }
        base.Dispose(disposing);
    }
    #endregion

    #region 配置重载读取与写入方法
    internal static Configuration Config = new();
    private static void ReloadConfig(ReloadEventArgs args = null!)
    {
        LoadConfig();
        args.Player.SendInfoMessage("[共同死亡事件]重新加载配置完毕。");
    }
    private static void LoadConfig()
    {
        Config = Configuration.Read();
        Config.Write();
    }
    #endregion

    #region 玩家进入服务器
    private void OnGreetPlayer(GreetPlayerEventArgs args)
    {
        var plr = TShock.Players[args.Who];
        if (plr == null || !plr.RealPlayer ||
            Config is null || !Config.Team) return;

        // 如果有缓存的队伍信息
        if (Config.BackTeam.TryGetValue(plr.Name, out var teamName))
        {
            int teamId = GetTeamId(teamName); // 获取队伍ID
            if (teamId != plr.Team && teamId > 0)
            {
                plr.SetTeam(teamId); // 设置玩家队伍
                plr.SendMessage($"已恢复您的队伍为{teamName}", 240, 250, 150);
            }
        }
    }
    #endregion

    #region 玩家离开服务器事件
    private void OnServerLeave(LeaveEventArgs args)
    {
        var plr = TShock.Players[args.Who];
        if (plr == null) return;

        // 只清理玩家自己的数据，不影响队伍其他成员
        ClearData(plr.Team, plr.Name);

        // 检查并清理激励数据
        if (Config.Team && Config.Incentive && plr.Team > -1)
        {
            ClearReward(plr, plr.Team);
        }
    }
    #endregion

    #region 玩家死亡事件
    private void OnKillMe(object? sender, GetDataHandlers.KillMeEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];

        if (plr is null || !plr.RealPlayer ||
            !Config.Enabled || Config is null) return;

        int team = Config.Team ? plr.Team : -1; // 确定队伍ID
        var teamName = GetTeamName(team); // 获取队伍名称

        var dead = GetDead(plr);
        if (dead.Count == 0)  // 检查是否是新的死亡事件开始
        {
            // 清理上次的数据（包括激励数据）
            ClearData(team, true);
            if (Config.Incentive && Config.Team)
                RemoveReward(team);

            // 更新死亡统计
            Config.AddDeath(plr.Name);
            Config.AddTeamDeath(teamName);
            Config.Write();

            string msg = Config.Team
                       ? $"\n————[c/508DC8:{plr.Name}]死亡————" +
                         $"\n{teamName}死亡{Config.GetTeamDeath(teamName)}次，" +
                         $"正在执行队伍死亡事件"
                       : $"\n————[c/508DC8:{plr.Name}]死亡————" +
                         $"\n个人死亡{Config.GetDeath(plr.Name)}次，" +
                         $"正在执行全体死亡事件";

            TSPlayer.All.SendMessage(msg, 240, 250, 150);
        }

        // 如果玩家不在死亡列表中，添加进去
        if (!dead.Contains(plr.Name))
        {
            dead.Add(plr.Name); // 标记玩家为已死亡
            plr.RespawnTimer = Config.RespawnTimer; // 设置重生计时器
        }

        var other = new HashSet<int>(); // 记录其他玩家以便激励
        var teamPly = new List<TSPlayer>(); // 同队伍玩家（用于激励）

        // 遍历所有在线玩家
        for (int i = 0; i < TShock.Players.Length; i++)
        {
            var p = TShock.Players[i];
            if (p == null || !p.RealPlayer || !p.Active ||
                Config.WhiteList.Contains(p.Name))
                continue;

            // 队伍模式下
            if (Config.Team)
            {
                if (p.Team == team)
                {
                    // 同队伍玩家
                    if (!dead.Contains(p.Name) && !p.Dead)
                    {
                        p.KillPlayer();
                        TSPlayer.All.SendData(PacketTypes.DeadPlayer, "", p.Index);
                    }

                    // 记录同队伍玩家用于激励（包括死亡玩家）
                    teamPly.Add(p);
                }
                else
                {
                    // 其他队伍玩家
                    if (!other.Contains(p.Index))
                        other.Add(p.Index);
                }
            }
            else
            {
                // 非队伍模式
                if (!dead.Contains(p.Name) && !p.Dead)
                {
                    p.KillPlayer();
                    TSPlayer.All.SendData(PacketTypes.DeadPlayer, "", p.Index);
                }
            }
        }

        // 激励其他队伍功能（只在第一个玩家死亡时执行）
        if (Config.Incentive && other.Count > 0 && dead.Count == 1)
        {
            Rewards.DoReward(plr, other, teamPly);
        }
    }
    #endregion

    #region 玩家重生补偿事件
    private void OnSpawn(object o, GetDataHandlers.SpawnEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];
        if (plr == null || !plr.RealPlayer || !Config.Enabled) return;

        SyncLife(plr); // 同步最大生命值

        var dead = GetDead(plr); // 获取死亡列表
        var resp = GetResp(plr); // 获取重生列表

        // 检查玩家是否在死亡列表中,不在则返回
        if (!dead.Contains(plr.Name)) return;

        int team = plr.Team;
        var teamName = GetTeamName(team); // 获取队伍名称

        // 显示死亡统计
        plr.SendMessage($"{teamName}[c/508DC8:{Config.GetTeamDeath(teamName)}]次," +
                        $"{plr.Name}[c/F5636F:{Config.GetDeath(plr.Name)}]次", Color.LightGreen);

        TimeSpan CoolTime = DateTime.UtcNow - GetCoolDown(plr);
        double CoolRemain = Config.CoolDowned - CoolTime.TotalSeconds;
        if (CoolRemain > 0)
        {
            string coolMsg = Config.Team ? $"{teamName}补尝冷却中，剩余: [c/508DC8:{CoolRemain:f2}]秒"
                                         : $"个人补尝冷却中，剩余: [c/508DC8:{CoolRemain:f2}]秒";

            plr.SendMessage(coolMsg, Color.White); // 提示冷却信息
            resp.Add(plr.Name); // 标记玩家已重生
            dead.Remove(plr.Name); // 移除玩家死亡标记
            return;
        }

        var mess = new StringBuilder();
        HandleSpawn(plr, mess); // 处理重生补偿
        string cont = mess.ToString(); // 获取补偿信息
        string exc = GetExc(plr); // 获取超出限制信息
        if (!string.IsNullOrEmpty(exc)) cont += exc; // 追加超出服务器限制信息

        // 检查是否是第一个重生的玩家
        if (resp.Count == 0)
        {
            SetCont(plr, cont); // 第一个重生的玩家，设置补偿内容
        }
        else
        {
            // 不是第一个重生的玩家，获取已记录的补偿内容
            string savedCont = GetCont(plr);
            cont = !string.IsNullOrEmpty(savedCont) ? savedCont : cont;
        }

        resp.Add(plr.Name);    // 标记玩家已重生
        dead.Remove(plr.Name); // 移除玩家死亡标记

        // 广播逻辑：当所有玩家都重生时（dead.Count == 0）
        if (dead.Count == 0 && resp.Count > 0)
        {
            // 重新获取补偿内容，确保正确
            string finalCont = GetCont(plr) ?? cont;
            string names = string.Join("、", resp);
            if (Config.Team)
            {
                string text = $"\n{teamName}死亡事件补尝(总次:{Config.GetTeamDeath(teamName)})：" +
                              $"{finalCont}" +
                              $"\n队伍名单：{names}";

                // 发送给同队伍内所有成员
                for (int i = 0; i < TShock.Players.Length; i++)
                {
                    var p = TShock.Players[i];
                    if (p != null && p.Team == team)
                    {
                        Tool.GradMess(p, text); // 逐行渐变
                    }
                }

                // 清理激励相关的临时数据（只在激励功能开启时）
                if (Config.Incentive)
                    RemoveReward(team);

                ClearData(team, true); // 清理队伍数据
            }
            else
            {
                string text = Tool.TextGradient($"\n共同死亡事件补尝：{finalCont}\n补尝名单：{names}");
                TSPlayer.All.SendMessage(text, Color.White); // 逐字渐变
                ClearData(-1, true); // 清理全体数据
            }

            SetCoolDown(plr); // 设置补偿冷却
            HandleExce(plr); // 处理超出服务器上限
        }
    }
    #endregion

    #region 玩家队伍变更事件
    private void OnPlayerTeam(object? sender, GetDataHandlers.PlayerTeamEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];
        if (plr == null || !plr.RealPlayer || !Config.Enabled) return;

        int oldTeam = plr.Team;
        int newTeam = e.Team;

        if (oldTeam == newTeam) return;

        if (Config.Team)
        {
            // 检查切换队伍冷却 不检查白名单玩家
            if (CheckSwitchCD(plr.Name) && !Config.WhiteList.Contains(plr.Name))
            {
                var coolT = GetSwitchCD(plr.Name);
                TimeSpan timeSpan = DateTime.UtcNow - coolT;
                double remain = Config.SwitchCD - timeSpan.TotalSeconds;
                plr.SendMessage($"队伍切换冷却中，请等待:[c/508DC8:{remain:f2}]秒", 240, 250, 150);
                e.Handled = true;
                plr.SetTeam(oldTeam); // 强制还原队伍
                return;
            }

            // 设置切换队伍冷却
            SetSwitchCD(plr.Name);

            // 从旧队伍移除玩家数据
            ClearData(oldTeam, plr.Name);

            // 清理旧队伍的激励数据
            if (Config.Incentive && oldTeam > -1)
            {
                ClearReward(plr, oldTeam);
            }

            // 提示玩家队伍变更信息
            string oldName = GetTeamName(oldTeam);
            string newName = GetTeamName(newTeam);
            plr.SendMessage($"您已从{oldName}切换到{newName}", 240, 250, 150);

            // 缓存玩家新队伍信息
            Config.BackTeam[plr.Name] = newName;
            Config.Write();

            // 获取新队伍数据,并提示队伍死亡次数
            var newData = GetData(newTeam);
            if (newData.Dead.Count > 0)
            {
                plr.SendMessage($"{newName}已有{newData.Dead.Count}名队员死亡", 240, 250, 150);
            }

            // 显示新队伍补偿冷却信息
            TimeSpan CoolTime = DateTime.UtcNow - newData.CoolDown;
            double CoolRemain = Config.CoolDowned - CoolTime.TotalSeconds;
            if (CoolRemain > 0)
            {
                plr.SendMessage($"{newName}补尝冷却剩余: [c/508DC8:{CoolRemain:f2}]秒", 240, 250, 150);
            }
        }
        else
        {
            // 非队伍模式，直接移除玩家
            ClearData(-1, plr.Name);
        }
    }
    #endregion

    #region 重生补偿方法
    private void HandleSpawn(TSPlayer plr, StringBuilder mess)
    {
        var tplr = plr.TPlayer;

        // 如果增加生命值大于0
        if (Config.AddLifeAmount > 0)
        {
            // 增加生命值
            tplr.statLife = tplr.statLifeMax += Config.AddLifeAmount;

            // 检查是否超过服务器最大生命值
            if (!Config.ExceMax && tplr.statLifeMax > TShock.Config.Settings.MaxHP)
                tplr.statLife = tplr.statLifeMax = TShock.Config.Settings.MaxHP;

            // 发送更新生命值数据包
            plr.SendData(PacketTypes.PlayerHp, null, plr.Index);
            mess.Append($"\n生命+{Config.AddLifeAmount},");
        }

        // 如果增加魔力值大于0
        if (Config.AddManaAmount > 0)
        {
            // 增加魔力值
            tplr.statMana = tplr.statManaMax += Config.AddManaAmount;

            // 检查是否超过服务器最大魔力值
            if (!Config.ExceMax && tplr.statManaMax > TShock.Config.Settings.MaxMP)
                tplr.statMana = tplr.statManaMax = TShock.Config.Settings.MaxMP;

            // 发送更新魔力值数据包
            plr.SendData(PacketTypes.PlayerMana, null, plr.Index);
            mess.Append($"\n魔力+{Config.AddManaAmount},");
        }

        // 如果命令列表不为空
        if (Config.DeathCommands is not null)
        {
            mess.Append($"\n执行命令: ");
            Group group = plr.Group; // 保存当前权限组

            try
            {
                // 临时提升为超级管理员执行命令
                plr.Group = new SuperAdminGroup();
                foreach (var cmd in Config.DeathCommands)
                {
                    // 执行命令
                    Commands.HandleCommand(plr, cmd);
                    mess.Append($"\n{cmd}");
                }
            }
            finally
            {
                // 总是恢复原权限组
                plr.Group = group;
            }
        }

        // 如果配置开启且物品列表不为空
        if (Config.GiveItem && Config.ItemList is not null)
        {
            mess.Append("\n获得物品: ");
            foreach (var item in Config.ItemList)
            {
                int itemType = item.Key;   // 物品ID
                int itemStack = item.Value; // 物品数量
                // 给予物品
                plr.GiveItem(itemType, itemStack);
                mess.Append($"[i/s{itemStack}:{itemType}] ");
            }
        }
    }
    #endregion

    #region 同步最大生命值的方法
    private void SyncLife(TSPlayer plr)
    {
        if (!Config.SyncLifeMax) return;

        // 缓存在线玩家中最大生命值的玩家
        int maxLife = 0;
        TSPlayer? maxPlr = null;

        // 遍历所有在线玩家
        for (int i = 0; i < TShock.Players.Length; i++)
        {
            var p = TShock.Players[i];
            // 跳过无效玩家、自己和白名单玩家
            if (p == null || !p.RealPlayer || !p.Active ||
                p.Index == plr.Index || Config.WhiteList.Contains(p.Name)) continue;

            // 开启队伍模式下，排除非队伍成员
            if (Config.Team && p.Team != plr.Team) continue;

            // 查找最大生命值玩家
            int curr = p.TPlayer.statLifeMax;
            if (curr > maxLife)
            {
                maxLife = curr;
                maxPlr = p;
            }
        }

        // 同步最大生命值,如果当前玩家的最大生命值小于找到的最大值
        if (maxPlr != null && plr.TPlayer.statLifeMax < maxLife)
        {
            // 更新玩家最大生命值
            plr.TPlayer.statLifeMax = maxLife;
            plr.SendData(PacketTypes.PlayerHp, null, plr.Index);
            var teamName = GetTeamName(plr.Team);

            string msg = Config.Team
                       ? $"已将您最大生命值,同步至当前{teamName}内数值最高玩家:[c/508DC8:{maxPlr.Name}]"
                       : $"已将您最大生命值,同步至数值最高在线玩家:[c/508DC8:{maxPlr.Name}]";

            plr.SendMessage(msg, 240, 250, 150);
        }
    }
    #endregion

    #region 处理超出服务器上限
    private static int maxHP = TShock.Config.Settings.MaxHP;
    private static int maxMP = TShock.Config.Settings.MaxMP;
    private static object lockObj = new object();
    private void HandleExce(TSPlayer plr)
    {
        if (!Config.ExceMax) return;

        var tplr = plr.TPlayer;
        StringBuilder? info = null;

        lock (lockObj)
        {
            // 检查并更新最大生命值
            if (tplr.statLifeMax > maxHP)
            {
                // 更新最大魔力值
                maxHP = tplr.statLifeMax;

                // 记录信息
                if (info is null) info = new StringBuilder();
                info.Append($"生命({maxHP})");
            }

            // 检查并更新最大魔力值
            if (tplr.statManaMax > maxMP)
            {
                // 更新最大魔力值
                maxMP = tplr.statManaMax;

                // 记录信息
                if (info is null) info = new StringBuilder();
                else info.Append(", ");
                info.Append($"魔力({maxMP})");
            }

            // 应用更改
            if (info is not null && info.Length > 0)
            {
                // 更新配置文件中的最大值
                TShock.Config.Settings.MaxHP = maxHP;
                TShock.Config.Settings.MaxMP = maxMP;

                // 异步写入配置文件
                Task.Run(() =>
                {
                    TShock.Config.Write(Path.Combine(TShock.SavePath, "config.json"));
                });

                // 记录超出信息到玩家数据
                string exc = GetExc(plr);
                string newExc = $"\n已提升上限: {info}";

                // 追加记录
                if (string.IsNullOrEmpty(exc))
                    SetExc(plr, newExc);
                else if (!exc.Contains(info.ToString()))
                    SetExc(plr, exc + $", {info}");
            }
        }
    }
    #endregion

}