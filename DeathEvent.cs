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
        string? teamCache = Cache.GetPlayerData(plr.Name).TeamName;
        if (!string.IsNullOrEmpty(teamCache))
        {
            int teamId = CacheData.GetTeamId(teamCache); // 获取队伍ID
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
            string msg = Cache.GetPlayerData(plr.Name).Lock ? "（已锁定）" : "";
            plr.SendMessage($"已恢复您的队伍为[c/508DC8:{Cache.GetPlayerData(plr.Name).TeamName}]" + msg, color);
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
        Cache.ClearTeamData(plr.Team, false, plr.Name);
        Cache.Write(); // 写入缓存

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
        var teamName = CacheData.GetTeamName(team); // 获取队伍名称
        var teamName2 = CacheData.GetTeamCName(plr.Team); // 获取带颜色的队伍名称

        var TData = Cache.GetTeamData(team);
        var dead = TData.Dead;
        if (dead.Count == 0)  // 检查是否是新的死亡事件开始
        {
            string msg = "";
            if (Config.Team)
            {
                if (plr.Team > -1)
                    TData.DeathCount++;

                msg += $"{teamName2}的[c/508DC8:{plr.Name}]死亡";

                if (Config.AllDead)
                    msg += $"\n1.执行{teamName2}集体死亡\n";

                if (Config.Incentive)
                    msg += "2.每人移除1件物品随机发给其他队";
            }
            else
            {
                msg += $"[c/508DC8:{plr.Name}]死亡\n";
                if (Config.AllDead)
                    msg += $"1.执行全服集体死亡";
            }

            TSPlayer.All.SendMessage($"\n{msg}\n", color);

            // 更新个人死亡次数
            Cache.GetPlayerData(plr.Name).DeathCount++;
            Cache.Write();   // 写入缓存数据
        }

        // 如果玩家不在死亡列表中，添加进去
        if (!dead.Contains(plr.Name))
        {
            dead.Add(plr.Name); // 标记玩家为已死亡
            Cache.Write();

            // 设置重生计时器
            if (Config.RespawnTimer > 0)
                plr.RespawnTimer = Config.RespawnTimer;
            else
                plr.RespawnTimer = TShock.Config.Settings.RespawnSeconds;
        }

        var other = new HashSet<int>(); // 记录其他玩家以便激励
        var teamPly = new List<TSPlayer>(); // 死亡同队伍玩家

        // 遍历所有在线玩家
        for (int i = 0; i < TShock.Players.Length; i++)
        {
            var p = TShock.Players[i];
            if (p == null || !p.RealPlayer || !p.Active) continue;

            // 队伍模式下
            if (Config.Team)
            {
                // 处理同队伍玩家
                if (p.Team == team)
                {
                    // 记录同队伍玩家,作为惩罚
                    if (Config.Incentive)
                        teamPly.Add(p);

                    // 只在集体死亡开启时杀死同队伍玩家
                    // 排除自己、管理员和白名单玩家
                    if (Config.AllDead && !dead.Contains(p.Name) && !p.Dead &&
                        !p.HasPermission(CMDs.Admin) &&
                        !Config.WhiteList.Contains(p.Name))
                    {
                        p.KillPlayer();
                        TSPlayer.All.SendData(PacketTypes.DeadPlayer, "", p.Index);
                    }
                }
                else if (Config.Incentive && !other.Contains(p.Index))
                {
                    other.Add(p.Index);  // 添加其他队伍玩家作为随机奖励对象
                }
            }
            else if (Config.AllDead)
            {
                // 非队伍模式，只在集体死亡开启时杀死所有玩家
                // 排除自己、管理员和白名单玩家
                if (!dead.Contains(p.Name) && !p.Dead &&
                    !p.HasPermission(CMDs.Admin) &&
                    !Config.WhiteList.Contains(p.Name))
                {
                    p.KillPlayer();
                    TSPlayer.All.SendData(PacketTypes.DeadPlayer, "", p.Index);
                }
            }
        }

        // 激励其他队伍功能
        if (other.Count > 0 && dead.Contains(plr.Name))
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

        // 获取队伍数据
        var TData = Cache.GetTeamData(plr);

        // 检查玩家是否在死亡列表中,不在则返回
        if (!TData.Dead.Contains(plr.Name)) return;

        // 获取玩家数据
        var PData = Cache.GetPlayerData(plr.Name);

        // 显示死亡统计
        plr.SendMessage($"\n{CacheData.GetTeamCName(plr.Team)}死亡[c/508DC8:{TData.DeathCount}]次," +
                        $"{plr.Name}死亡[c/F5636F:{PData.DeathCount}]次", Color.LightGreen);

        // 检查补偿冷却时间
        TimeSpan CoolTime = DateTime.Now - PData.CoolDown;
        double CoolRemain = Config.CoolDowned - CoolTime.TotalSeconds;
        if (CoolRemain > 0)
        {
            plr.SendMessage($"补尝冷却剩余: [c/508DC8:{CoolRemain:f2}]秒", Color.White); // 提示冷却信息
            TData.Dead.Remove(plr.Name); // 移除玩家死亡标记
            Cache.Write();
            return;
        }

        TData.Resp.Add(plr.Name); // 标记玩家已重生
        Cache.Write();

        if (TData.Resp.Contains(plr.Name))
        {
            var mess = new StringBuilder();
            HandleSpawn(plr, mess);
            string cont = mess.ToString(); // 补偿信息
            string exc = "";
            exc = HandleExce(plr, exc); // 处理超出服务器上限的信息
            plr.SendMessage(TextGradient($"\n{plr.Name}重生补尝列表:{cont + exc}\n"), Color.White); // 逐字渐变
            PData.CoolDown = DateTime.Now;
            TData.Resp.Remove(plr.Name); // 移除重生标记
            TData.Dead.Remove(plr.Name); // 移除玩家死亡标记
            Cache.Write();
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
        var PData = Cache.GetPlayerData(plr.Name);

        // 检查队伍锁定
        if (PData.Lock)
        {
            plr.SendMessage($"你的队伍已被锁定为{PData.TeamName},无法切换", Color.Red);
            e.Handled = true;
            plr.SetTeam(oldTeam);
            return;
        }

        // 检查切换队伍冷却 不检查白名单玩家
        if (Cache.CheckSwitchCD(plr, PData))
        {
            TimeSpan timeSpan = DateTime.Now - PData.SwitchTime!.Value;
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

            var teamName2 = CacheData.GetTeamCName(newTeam); // 带颜色的队伍名称
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
                plr.SendMessage($"已加入{teamName2}（队伍内无其他成员）", color);
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
                Time = Config.VoteTime,
            };

            if (Vote.Add(vote))
            {
                // 通知目标队伍成员
                string applyMsg = $"[c/508DC8:{plr.Name}]申请加入{teamName2},\n" +
                                  $"请使用 /det [c/5A9CDE:y] 或 /det [c/F4636F:n] 投票({Config.VoteTime}秒)";

                foreach (var p in target)
                {
                    p.SendMessage(applyMsg, color);
                }

                plr.SendMessage($"已向{teamName2}发送申请，等待投票结果", color);
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
        Cache.ClearTeamData(oldTeam, false, plr.Name);

        // 如果不是来自事件调用，手动设置队伍(事件自己会设置队伍)
        if (!fromEvent)
            plr.SetTeam(newTeam);

        // 获取队伍名称
        var oldName = CacheData.GetTeamName(oldTeam);
        var oldName2 = CacheData.GetTeamCName(oldTeam);
        var newName = CacheData.GetTeamName(newTeam);
        var newName2 = CacheData.GetTeamCName(newTeam);

        // 发送消息
        string mess = $"[c/508DC8:{plr.Name}] 已从{oldName2}加入到{newName2}";
        TSPlayer.All.SendMessage(mess, color);

        // 更新缓存
        var pdata = Cache.GetPlayerData(plr.Name);
        pdata.SwitchTime = DateTime.Now;
        pdata.TeamName = newName;
        Cache.Write();

        // 获取新队伍数据并提示
        var newData = Cache.GetTeamData(newTeam);
        if (newData.Dead.Count > 0)
            plr.SendMessage($"{newName2}已有{newData.Dead.Count}名队员死亡", color);
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

            // 跳过无效玩家、自己和白名单玩家、管理员
            if (p == null || !p.RealPlayer || !p.Active ||
                p.Index == plr.Index || p.HasPermission(CMDs.Admin) ||
                Config.WhiteList.Contains(p.Name)) continue;

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
            var teamName2 = CacheData.GetTeamCName(plr.Team);

            string msg = Config.Team
                       ? $"已将您最大生命值,同步至当前{teamName2}内数值最高玩家:[c/508DC8:{maxPlr.Name}]"
                       : $"已将您最大生命值,同步至数值最高在线玩家:[c/508DC8:{maxPlr.Name}]";

            plr.SendMessage(msg, color);
        }
    }
    #endregion

    #region 处理超出上限的方法
    private static int maxHP = TShock.Config.Settings.MaxHP;
    private static int maxMP = TShock.Config.Settings.MaxMP;
    private static object lockObj = new object();
    private static string HandleExce(TSPlayer plr, string exc)
    {
        if (Config.ExceMax)
        {
            var tplr = plr.TPlayer;
            StringBuilder? info = null;

            lock (lockObj)
            {
                if (tplr.statLifeMax > maxHP)
                {
                    maxHP = tplr.statLifeMax;
                    if (info is null) info = new StringBuilder();
                    info.Append($"生命({maxHP})");
                }

                if (tplr.statManaMax > maxMP)
                {
                    maxMP = tplr.statManaMax;
                    if (info is null) info = new StringBuilder();
                    else info.Append(", ");
                    info.Append($"魔力({maxMP})");
                }

                if (info is not null && info.Length > 0)
                {
                    TShock.Config.Settings.MaxHP = maxHP;
                    TShock.Config.Settings.MaxMP = maxMP;

                    Task.Run(() =>
                    {
                        TShock.Config.Write(Path.Combine(TShock.SavePath, "config.json"));
                    });

                    exc = $"\n已提升上限: {info}";
                }
            }
        }

        return exc;
    }
    #endregion
}