using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using static DeathEvent.Tool;

namespace DeathEvent;

[ApiVersion(2, 1)]
public class DeathEvent : TerrariaPlugin
{
    #region 插件信息
    public override string Name => "共同死亡事件";
    public override string Author => "Kimi,羽学";
    public override Version Version => new(1, 0, 6);
    public override string Description => "玩家死亡实现共同死亡事件,允许重生后实现补偿,支持队伍模式";
    #endregion

    #region 注册与释放
    public DeathEvent(Main game) : base(game) { }
    public override void Initialize()
    {
        if (!Directory.Exists(Configuration.Paths))
        {
            Directory.CreateDirectory(Configuration.Paths);
        }

        LoadConfig();
        GeneralHooks.ReloadEvent += ReloadConfig;
        GetDataHandlers.PlayerSpawn += OnSpawn!;
        GetDataHandlers.KillMe += OnKillMe;
        GetDataHandlers.PlayerTeam += OnPlayerTeam;
        ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
        ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreetPlayer);
        GetDataHandlers.PlayerUpdate.Register(this.OnPlayerUpdate);
        ServerApi.Hooks.GameUpdate.Register(this, OnUpdate);
        Commands.ChatCommands.Add(new TShockAPI.Command("det.use", CMDs.CMD, "det", "共同死亡"));
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
            GetDataHandlers.PlayerUpdate.UnRegister(this.OnPlayerUpdate);
            ServerApi.Hooks.GameUpdate.Deregister(this, OnUpdate);
            Commands.ChatCommands.RemoveAll(x => x.CommandDelegate == CMDs.CMD);
        }
        base.Dispose(disposing);
    }
    #endregion

    #region 配置重载读取与写入方法
    internal static Configuration Config = new();
    internal static CacheData Cache => Config.DeathCache;
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
            Config is null || !Config.Enabled || !Config.Team) return;

        // 如果有缓存的队伍信息
        string? teamCache = Cache.GetData(plr.Name).TeamCache;
        if (!string.IsNullOrEmpty(teamCache))
        {
            int teamId = Data.GetTeamId(teamCache); // 获取队伍ID
            if (teamId != plr.Team && teamId > 0)
            {
                plr.SetTeam(teamId); // 设置玩家队伍
                plr.SetData("join", true); // 标记为刚加入
            }
        }
    }
    #endregion

    #region 玩家更新事件,用于提示恢复队伍信息
    private void OnPlayerUpdate(object? sender, GetDataHandlers.PlayerUpdateEventArgs e)
    {
        var plr = e.Player;
        if (plr == null || !plr.RealPlayer ||
            Config is null || !Config.Enabled || !Config.Team) return;

        // 检查玩家是否刚加入
        if (plr.GetData<bool>("join"))
        {
            // 发送恢复队伍消息
            plr.SendMessage($"已恢复您的队伍为[c/508DC8:{Cache.GetData(plr.Name).TeamCache}]", color);
            plr.RemoveData("join"); // 移除加入标记
            return;
        }
    }
    #endregion

    #region 游戏更新事件 检查投票是否超时
    private int frame = 0;
    private int Interval = 60;
    private void OnUpdate(EventArgs args)
    {
        frame++; // 帧计数增加

        // 动态调整间隔：有投票时每秒检查，无投票时每分钟检查
        bool hasVotes = Vote.VoteData.Count > 0;
        Interval = hasVotes ? 60 : 3600;

        // 达到间隔时间再执行
        if (frame < Interval) return;

        frame = 0; // 重置帧计数

        // 检查投票时间（内部会先检查是否有超时投票）
        Vote.CheckTimeout();
    }
    #endregion

    #region 玩家离开服务器事件
    private void OnServerLeave(LeaveEventArgs args)
    {
        var plr = TShock.Players[args.Who];
        if (plr == null || !Config.Enabled || !Config.Team) return;

        // 只清理玩家自己的数据，不影响队伍其他成员
        Data.ClearTeamData(plr.Team, plr.Name);

        // 清理玩家的投票数据
        if (Config.TeamApply)
        {
            // 清理玩家作为申请人的投票
            Vote.Clear(plr.Name);
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
        var teamName = Data.GetTeamName(team); // 获取队伍名称

        var dead = Data.GetTeamData(plr).Dead;
        if (dead.Count == 0)  // 检查是否是新的死亡事件开始
        {
            // 清理上次的队伍数据
            Data.ClearTeamData(team, true);

            // 更新死亡统计
            var pData = Cache.GetData(plr.Name);
            pData.DeathCount++;
            int deathCount = pData.DeathCount;
            // 更新队伍死亡次数
            Cache.TeamDeathCount[teamName] = Cache.GetTeamDeath(teamName) + 1;
            Cache.Write();   // 写入缓存数据

            // 使用三目运算符简化
            string msg = Config.Team
                       ? $"{teamName}死亡{Cache.GetTeamDeath(teamName)}次，正在执行队伍死亡事件"
                       : $"个人死亡{deathCount}次，正在执行全体死亡事件";

            TSPlayer.All.SendMessage($"\n————[c/508DC8:{plr.Name}]死亡————\n{msg}", color);
        }

        // 如果玩家不在死亡列表中，添加进去
        if (!dead.Contains(plr.Name))
        {
            dead.Add(plr.Name); // 标记玩家为已死亡
            plr.RespawnTimer = Config.RespawnTimer; // 设置重生计时器
        }

        var other = new HashSet<int>(); // 记录其他玩家以便激励
        var teamPly = new List<TSPlayer>(); // 死亡同队伍玩家

        // 遍历所有在线玩家
        for (int i = 0; i < TShock.Players.Length; i++)
        {
            var p = TShock.Players[i];
            if (p == null || !p.RealPlayer || !p.Active ||
                Config.WhiteList.Contains(p.Name) ||
                plr.HasPermission(CMDs.Admin))
                continue;

            // 队伍模式下
            if (Config.Team)
            {
                if (p.Team == team)
                {
                    // 记录同队伍玩家用于激励
                    teamPly.Add(p);

                    // 杀死所有同队伍没死亡的玩家
                    if (!dead.Contains(p.Name) && !p.Dead)
                    {
                        p.KillPlayer();
                        TSPlayer.All.SendData(PacketTypes.DeadPlayer, "", p.Index);
                    }
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

        var data = Data.GetTeamData(plr); // 获取玩家数据
        var dead = data.Dead; // 获取死亡列表
        var resp = data.Resp; // 获取重生列表

        // 检查玩家是否在死亡列表中,不在则返回
        if (!dead.Contains(plr.Name)) return;

        int team = plr.Team;
        var teamName = Data.GetTeamName(team); // 获取队伍名称

        // 显示死亡统计
        plr.SendMessage($"{teamName}[c/508DC8:{Cache.GetTeamDeath(teamName)}]次," +
                        $"{plr.Name}[c/F5636F:{Cache.GetData(plr.Name).DeathCount}]次", Color.LightGreen);

        TimeSpan CoolTime = DateTime.UtcNow - data.CoolDown;
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
        string exc = data.Exc; // 获取超出限制信息
        if (!string.IsNullOrEmpty(exc)) cont += exc; // 追加超出服务器限制信息

        // 检查是否是第一个重生的玩家
        if (resp.Count == 0)
        {
            data.Cont = cont;  // 第一个重生的玩家，设置补偿内容
        }
        else
        {
            // 不是第一个重生的玩家，获取已记录的补偿内容
            string savedCont = data.Exc;
            cont = !string.IsNullOrEmpty(savedCont) ? savedCont : cont;
        }

        resp.Add(plr.Name);    // 标记玩家已重生
        dead.Remove(plr.Name); // 移除玩家死亡标记

        // 广播逻辑：当所有玩家都重生时（dead.Count == 0）
        if (dead.Count == 0 && resp.Count > 0)
        {
            // 重新获取补偿内容，确保正确
            string finalCont = data.Exc ?? cont;
            string names = string.Join("、", resp);
            if (Config.Team)
            {
                string text = $"\n{teamName}死亡事件补尝(总次:{Cache.GetTeamDeath(teamName)})：" +
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

                Data.ClearTeamData(team, true); // 清理队伍数据
            }
            else
            {
                string text = Tool.TextGradient($"\n共同死亡事件补尝：{finalCont}\n补尝名单：{names}");
                TSPlayer.All.SendMessage(text, Color.White); // 逐字渐变
                Data.ClearTeamData(-1, true); // 清理全体数据
            }

            data.CoolDown = DateTime.UtcNow; // 设置补偿冷却
            HandleExce(plr); // 处理超出服务器上限
        }
    }
    #endregion

    #region 玩家队伍变更事件
    private void OnPlayerTeam(object? sender, GetDataHandlers.PlayerTeamEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];
        if (plr == null || !plr.RealPlayer || !Config.Enabled || !Config.Team) return;

        int oldTeam = plr.Team;
        int newTeam = e.Team;

        if (oldTeam == newTeam) return;

        // 获取玩家缓存数据
        var pdata = Cache.GetData(plr.Name);

        // 检查切换队伍冷却 不检查白名单玩家
        if (Cache.CheckSwitchCD(plr, pdata))
        {
            TimeSpan timeSpan = DateTime.Now - pdata.SwitchTime!.Value;
            double remain = Config.SwitchCD - timeSpan.TotalSeconds;
            plr.SendMessage($"队伍切换冷却中，请等待:[c/508DC8:{remain:f2}]秒", color);
            e.Handled = true;
            plr.SetTeam(oldTeam); // 强制还原队伍
            return;
        }

        // 如果队伍申请功能已经处理了，就直接返回，不执行后续的缓存更新
        if (TeamApply(e, plr, oldTeam, newTeam))
        {
            return;
        }

        // 切换队伍
        SwitchTeam(plr, newTeam);
    }
    #endregion

    #region 队伍申请功能
    private static bool TeamApply(GetDataHandlers.PlayerTeamEventArgs e, TSPlayer plr, int oldTeam, int newTeam)
    {
        // 检查玩家是否拥有直接切换权限（白名单或det.admin权限）
        bool hasPerm = Config.WhiteList.Contains(plr.Name) || plr.HasPermission(CMDs.Admin);
        if (hasPerm) return false; // 直接允许切换队伍

        if (Config.TeamApply && newTeam > 0) // newTeam > 0 表示不是无队伍
        {
            // 检查玩家是否已有未结束的申请
            if (Vote.HasPending(plr.Name))
            {
                plr.SendMessage("您已有未完成的队伍申请，请等待投票结束", Color.Yellow);
                e.Handled = true;
                plr.SetTeam(oldTeam);
                return true;
            }

            // 检查目标队伍是否已有未结束的投票
            if (Vote.HasTeam(newTeam))
            {
                plr.SendMessage("该队伍已有申请投票正在进行，请稍后再试", Color.Yellow);
                e.Handled = true;
                plr.SetTeam(oldTeam);
                return true;
            }

            var teamName = Data.GetTeamName(newTeam);
            var target = new List<TSPlayer>();

            // 一次性收集目标队伍成员
            for (int i = 0; i < TShock.Players.Length; i++)
            {
                var p = TShock.Players[i];
                if (p != null && p.Active && p.Team == newTeam && p.Index != plr.Index)
                {
                    target.Add(p);
                }
            }

            // 如果目标队伍没有其他成员，直接允许加入
            if (target.Count == 0)
            {
                plr.SendMessage($"已加入{teamName}（队伍内无其他成员）", color);
                return false; // 返回false，让后续逻辑处理正常的队伍切换
            }

            // 还原队伍
            e.Handled = true;
            plr.SetTeam(oldTeam);

            // 创建投票
            var vote = new Vote.TeamVote
            {
                AppName = plr.Name,
                Team = newTeam,
                Start = DateTime.Now,
                Time = Config.VoteTime
            };

            if (Vote.Add(vote))
            {
                // 通知目标队伍成员
                string applyMsg = $"[c/508DC8:{plr.Name}]申请加入{teamName},\n" +
                                  $"请使用 /det [c/5A9CDE:y] 或 /det [c/F4636F:n] 投票({Config.VoteTime}秒)";

                foreach (var p in target)
                {
                    p.SendMessage(applyMsg, color);
                }

                plr.SendMessage($"已向{teamName}发送申请，等待投票结果", color);
            }
            else
            {
                plr.SendMessage("申请创建失败，请稍后再试", Color.Yellow);
            }
            return true;
        }

        return false;
    }
    #endregion

    #region 队伍切换方法
    public static void SwitchTeam(TSPlayer plr, int newTeam, bool fromEvent = true)
    {
        int oldTeam = plr.Team;

        // 从旧队伍移除玩家数据
        Data.ClearTeamData(oldTeam, plr.Name);

        // 如果不是来自事件调用，直接设置队伍
        if (!fromEvent)
        plr.SetTeam(newTeam);

        // 获取队伍名称
        var oldName = Data.GetTeamName(oldTeam);
        var newName = Data.GetTeamName(newTeam);

        // 发送消息
        string mess = fromEvent
            ? $"[c/508DC8:{plr.Name}] 已从{oldName}加入到{newName}"
            : $"[c/508DC8:{plr.Name}] 已加入 {newName}";

        TSPlayer.All.SendMessage(mess, color);

        // 更新缓存
        var pdata = Cache.GetData(plr.Name);
        pdata.SwitchTime = DateTime.Now;
        pdata.TeamCache = newName;
        Cache.Write();

        // 获取新队伍数据并提示
        var newData = Data.GetTeamData(newTeam);
        if (newData.Dead.Count > 0)
            plr.SendMessage($"{newName}已有{newData.Dead.Count}名队员死亡", color);

        // 显示冷却信息
        TimeSpan coolTime = DateTime.UtcNow - newData.CoolDown;
        double coolRemain = Config.CoolDowned - coolTime.TotalSeconds;
        if (coolRemain > 0)
            plr.SendMessage($"{newName}补尝冷却剩余: [c/508DC8:{coolRemain:f2}]秒", color);
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
            var teamName = Data.GetTeamName(plr.Team);

            string msg = Config.Team
                       ? $"已将您最大生命值,同步至当前{teamName}内数值最高玩家:[c/508DC8:{maxPlr.Name}]"
                       : $"已将您最大生命值,同步至数值最高在线玩家:[c/508DC8:{maxPlr.Name}]";

            plr.SendMessage(msg, color);
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
        var data = Data.GetTeamData(plr);
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
                string exc = data.Exc;
                string newExc = $"\n已提升上限: {info}";

                // 追加记录
                if (string.IsNullOrEmpty(exc))
                    data.Exc = newExc;
                else if (!exc.Contains(info.ToString()))
                    data.Exc = exc + $", {info}";
            }
        }
    }
    #endregion
}