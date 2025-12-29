using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace DeathEvent;

[ApiVersion(2, 1)]
public class DeathEvent : TerrariaPlugin
{
    #region 插件信息
    public override string Name => "共同死亡事件";
    public override string Author => "Kimi,羽学";
    public override Version Version => new(1, 0, 2);
    public override string Description => "玩家死亡实现共同死亡事件,允许重生后实现补偿";
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

    #region 数据结构
    // 非队伍模式使用全局数据
    private HashSet<string> Dead = new HashSet<string>();
    private HashSet<string> Respawn = new HashSet<string>();
    private string Content = "";
    private string ExceMess = "";
    private Dictionary<string, DateTime> CoolTime = new Dictionary<string, DateTime>();

    // 队伍模式按队伍存储数据
    private Dictionary<int, TeamDatas> TeamData = new Dictionary<int, TeamDatas>();
    public class TeamDatas
    {
        public HashSet<string> Dead = new HashSet<string>();
        public HashSet<string> Respawn = new HashSet<string>();
        public string Content = "";
        public string ExceMess = "";
        public DateTime CoolTime = DateTime.UtcNow;
    }
    #endregion

    #region 玩家死亡事件
    private void OnKillMe(object? sender, GetDataHandlers.KillMeEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];

        if (plr is null || !plr.RealPlayer ||
            !Config.Enabled || Config is null) return;

        var dead = GetDead(plr);
        var respawn = GetRespawn(plr);

        if (dead.Count == 0)
        {
            string mess;
            if (Config.Team)
            {
                string teamName = GetTeamName(plr.Team);
                mess = $"[c/508DC8:{plr.Name}]({teamName})死亡，正在执行队伍死亡事件";
            }
            else
            {
                mess = $"[c/508DC8:{plr.Name}]死亡，正在执行共同死亡事件";
            }

            TSPlayer.All.SendMessage(mess, 240, 250, 150);

            if (respawn.Count != 0)
                respawn.Clear();
        }

        if (!dead.Contains(plr.Name))
        {
            dead.Add(plr.Name);
            plr.RespawnTimer = Config.RespawnTimer;
        }

        var other = TShock.Players.Where(p => p is not null && p.RealPlayer && p.Active && !p.Dead);
        foreach (var p in other)
        {
            if (dead.Contains(p.Name) ||
                Config.WhiteList.Contains(p.Name)) continue;

            if (Config.Team && p.Team != plr.Team) continue;

            p.KillPlayer();
            TSPlayer.All.SendData(PacketTypes.DeadPlayer, "", p.Index);
        }
    }
    #endregion

    #region 玩家重生补偿事件
    private void OnSpawn(object o, GetDataHandlers.SpawnEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];

        if (plr == null || !plr.RealPlayer ||
            !Config.Enabled || Config is null) return;

        if (Config.SyncLifeMax)
        {
            SyncMaxLife(plr);
        }

        var dead = GetDead(plr);
        var respawn = GetRespawn(plr);
        var content = GetContent(plr);
        var exceMess = GetExceMess(plr);

        if (dead.Contains(plr.Name))
        {
            DateTime coolTime = GetCoolTime(plr);
            TimeSpan timeSpan = DateTime.UtcNow - coolTime;
            double remaining = Config.CoolDowned - timeSpan.TotalSeconds;
            if (remaining > 0)
            {
                string teamName = GetTeamName(plr.Team);
                string coolMsg = Config.Team ? $"[{teamName}]补尝冷却中，剩余时间: [c/508DC8:{remaining:f2}]秒" :
                                               $"补尝冷却中，剩余时间: [c/508DC8:{remaining:f2}]秒";

                plr.SendMessage(coolMsg, Color.White);
                respawn.Add(plr.Name);
                dead.Remove(plr.Name);
                return;
            }

            var mess = new StringBuilder();
            HandleSpawn(plr, mess);

            if (respawn.Count == 0)
            {
                content = mess.ToString();

                if (!string.IsNullOrEmpty(exceMess))
                {
                    content += $"{exceMess}";
                }

                SetContent(plr, content);
            }

            respawn.Add(plr.Name);
            dead.Remove(plr.Name);
        }

        if (Config.Broadcast && dead.Count == 0 && respawn.Count > 0)
        {
            if (!string.IsNullOrEmpty(content))
            {
                var names = string.Join("、", respawn);

                string prefix = Config.Team ? $"{GetTeamName(plr.Team)}" : "";
                var text = $"\n{prefix}补尝内容：{content}" + $"\n补尝名单：{names}";

                if (Config.Team)
                {
                    foreach (var other in TShock.Players)
                    {
                        if (other != null && other.Team == plr.Team)
                        {
                            other.SendMessage(Tool.TextGradient(text), Color.Yellow);
                        }
                    }
                }
                else
                {
                    TShock.Utils.Broadcast(Tool.TextGradient(text), Color.Yellow);
                }
            }

            if (Config.Team)
            {
                ClearTeamData(plr.Team);
            }
            else
            {
                Respawn.Clear();
                Content = string.Empty;
                ExceMess = string.Empty;
            }
        }
    }
    #endregion

    #region 重生补偿方法
    private void HandleSpawn(TSPlayer plr, StringBuilder mess)
    {
        var tplr = plr.TPlayer;

        if (Config.AddLifeAmount > 0)
        {
            tplr.statLife = tplr.statLifeMax += Config.AddLifeAmount;

            mess.Append($"\n生命上限 +{Config.AddLifeAmount},");

            if (!Config.ExceMax && tplr.statLifeMax > TShock.Config.Settings.MaxHP)
            {
                tplr.statLife = tplr.statLifeMax = TShock.Config.Settings.MaxHP;
            }

            plr.SendData(PacketTypes.PlayerHp, null, plr.Index);
            plr.SendData(PacketTypes.PlayerInfo, null, plr.Index);
        }

        if (Config.AddManaAmount > 0)
        {
            tplr.statMana = tplr.statManaMax += Config.AddManaAmount;

            mess.Append($"\n魔力上限 +{Config.AddManaAmount},");

            if (!Config.ExceMax && tplr.statManaMax > TShock.Config.Settings.MaxMP)
            {
                tplr.statMana = tplr.statManaMax = TShock.Config.Settings.MaxMP;
            }

            plr.SendData(PacketTypes.PlayerMana, null, plr.Index);
            plr.SendData(PacketTypes.PlayerInfo, null, plr.Index);
        }

        if (Config.ExceMax)
        {
            var Info = new StringBuilder();
            string TSConfig = Path.Combine(TShock.SavePath, "config.json");

            if (tplr.statLifeMax > TShock.Config.Settings.MaxHP)
            {
                TShock.Config.Settings.MaxHP = tplr.statLifeMax;
                if (Info.Length > 0) Info.Append(", ");
                Info.Append($"生命上限({tplr.statLifeMax})");
            }

            if (tplr.statManaMax > TShock.Config.Settings.MaxMP)
            {
                TShock.Config.Settings.MaxMP = tplr.statManaMax;
                if (Info.Length > 0) Info.Append(", ");
                Info.Append($"魔力上限({tplr.statManaMax})");
            }

            if (Info.Length > 0)
            {
                TShock.Config.Write(TSConfig);

                var exceMess = GetExceMess(plr);
                if (string.IsNullOrEmpty(exceMess))
                {
                    SetExceMess(plr, $"\n已提升服务器上限: {Info}");
                }
                else if (!exceMess.Contains(Info.ToString()))
                {
                    SetExceMess(plr, exceMess + $", {Info}");
                }
            }
        }

        if (Config.DeathCommands is not null)
        {
            mess.Append($"\n执行命令: ");
            Group group = plr.Group;

            try
            {
                plr.Group = new SuperAdminGroup();
                foreach (var cmd in Config.DeathCommands)
                {
                    Commands.HandleCommand(plr, cmd);
                    mess.Append($"\n{cmd}");
                }
            }
            finally
            {
                plr.Group = group;
            }
        }

        if (Config.GiveItem && Config.ItemList is not null)
        {
            mess.Append("\n获得物品: ");
            foreach (var item in Config.ItemList)
            {
                int itemType = item.Key;
                int itemStack = item.Value;
                plr.GiveItem(itemType, itemStack);
                mess.Append($"[i/s{itemStack}:{itemType}] ");
            }
        }

        SetCoolTime(plr);
    }
    #endregion

    #region 同步最大生命值的方法
    private void SyncMaxLife(TSPlayer plr)
    {
        var players = Config.Team
            ? TShock.Players.Where(p => p is not null &&
                                       p.RealPlayer &&
                                       p.Active &&
                                       p.Team == plr.Team &&
                                       !Config.WhiteList.Contains(p.Name))
            : TShock.Players.Where(p => p is not null &&
                                       p.RealPlayer &&
                                       p.Active &&
                                       !Config.WhiteList.Contains(p.Name));

        TSPlayer? other = null;
        int maxLife = 0;

        foreach (var op in players)
        {
            if (op.Index == plr.Index) continue;

            int curr = op.TPlayer.statLifeMax;
            if (curr > maxLife)
            {
                maxLife = curr;
                other = op;
            }
        }

        if (other != null && plr.TPlayer.statLifeMax < maxLife)
        {
            plr.TPlayer.statLifeMax = maxLife;
            plr.TPlayer.statLife = maxLife;

            plr.SendData(PacketTypes.PlayerHp, null, plr.Index);
            plr.SendData(PacketTypes.PlayerInfo, null, plr.Index);

            string mess;
            if (Config.Team)
            {
                string teamName = GetTeamName(plr.Team);
                mess = $"已将您最大生命值,同步至当前{teamName}内数值最高玩家:[c/508DC8:{other.Name}]";
            }
            else
            {
                mess = $"已将您最大生命值,同步至数值最高在线玩家:[c/508DC8:{other.Name}]";
            }

            plr.SendMessage(mess, 240, 250, 150);
        }
    }
    #endregion

    #region 玩家离开服务器事件
    private void OnServerLeave(LeaveEventArgs args)
    {
        var plr = TShock.Players[args.Who];
        if (plr == null) return;

        if (!Config.Team)
        {
            if (Dead.Contains(plr.Name))
            {
                Dead.Remove(plr.Name);

                if (Dead.Count == 0)
                {
                    Respawn.Clear();
                    Content = string.Empty;
                    ExceMess = string.Empty;
                }
            }
        }
        else
        {
            int teamId = plr.Team;

            if (TeamData.ContainsKey(teamId) && TeamData[teamId].Dead.Contains(plr.Name))
            {
                TeamData[teamId].Dead.Remove(plr.Name);

                if (TeamData[teamId].Dead.Count == 0)
                {
                    ClearTeamData(teamId);
                }
            }

            if (TeamData.ContainsKey(teamId) && TeamData[teamId].Respawn.Contains(plr.Name))
            {
                TeamData[teamId].Respawn.Remove(plr.Name);
            }
        }

        if (CoolTime.ContainsKey(plr.Name))
        {
            CoolTime.Remove(plr.Name);
        }
    }
    #endregion

    #region 玩家队伍变更事件
    private void OnPlayerTeam(object? sender, GetDataHandlers.PlayerTeamEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];
        if (plr == null || !plr.RealPlayer ||
            !Config.Enabled || Config is null) return;

        int oldTeam = plr.Team;
        int newTeam = e.Team;

        if (oldTeam == newTeam) return;

        if (Config.Team)
        {
            if (TeamData.ContainsKey(oldTeam) && TeamData[oldTeam].Dead.Contains(plr.Name))
            {
                TeamData[oldTeam].Dead.Remove(plr.Name);

                if (TeamData[oldTeam].Dead.Count == 0)
                {
                    ClearTeamData(oldTeam);
                }
            }

            if (TeamData.ContainsKey(oldTeam) && TeamData[oldTeam].Respawn.Contains(plr.Name))
            {
                TeamData[oldTeam].Respawn.Remove(plr.Name);
            }

            if (CoolTime.ContainsKey(plr.Name))
            {
                CoolTime.Remove(plr.Name);
            }

            string oldName = GetTeamName(oldTeam);
            string newName = GetTeamName(newTeam);
            plr.SendMessage($"您已从{oldName}切换到{newName}", 240, 250, 150);

            if (TeamData.ContainsKey(newTeam) && TeamData[newTeam].Dead.Count > 0)
            {
                int deadCount = TeamData[newTeam].Dead.Count;
                plr.SendMessage($"注意：您的新队伍{newName}正在经历死亡事件，已有{deadCount}名队员死亡", 240, 250, 150);
            }

            if (TeamData.ContainsKey(newTeam))
            {
                var timeSpan = DateTime.UtcNow - TeamData[newTeam].CoolTime;
                var remaining = Config.CoolDowned - timeSpan.TotalSeconds;
                if (remaining > 0)
                {
                    plr.SendMessage($"您的新队伍{newName}补尝冷却中，剩余时间: [c/508DC8:{remaining:f2}]秒", 240, 250, 150);
                }
            }
        }
        else
        {
            if (Dead.Contains(plr.Name))
            {
                Dead.Remove(plr.Name);

                if (Dead.Count == 0)
                {
                    Respawn.Clear();
                    Content = string.Empty;
                    ExceMess = string.Empty;
                }
            }
        }
    }
    #endregion

    #region 获取队伍数据的方法
    private HashSet<string> GetDead(TSPlayer plr)
    {
        if (!Config.Team) return Dead;

        if (!TeamData.ContainsKey(plr.Team))
            TeamData[plr.Team] = new TeamDatas();

        return TeamData[plr.Team].Dead;
    }

    private HashSet<string> GetRespawn(TSPlayer plr)
    {
        if (!Config.Team) return Respawn;

        if (!TeamData.ContainsKey(plr.Team))
            TeamData[plr.Team] = new TeamDatas();

        return TeamData[plr.Team].Respawn;
    }

    private string GetContent(TSPlayer plr)
    {
        if (!Config.Team) return Content;

        if (!TeamData.ContainsKey(plr.Team))
            TeamData[plr.Team] = new TeamDatas();

        return TeamData[plr.Team].Content;
    }

    private void SetContent(TSPlayer plr, string content)
    {
        if (!Config.Team)
        {
            Content = content;
        }
        else
        {
            if (!TeamData.ContainsKey(plr.Team))
                TeamData[plr.Team] = new TeamDatas();

            TeamData[plr.Team].Content = content;
        }
    }

    private string GetExceMess(TSPlayer plr)
    {
        if (!Config.Team) return ExceMess;

        if (!TeamData.ContainsKey(plr.Team))
            TeamData[plr.Team] = new TeamDatas();

        return TeamData[plr.Team].ExceMess;
    }

    private void SetExceMess(TSPlayer plr, string exceMess)
    {
        if (!Config.Team)
        {
            ExceMess = exceMess;
        }
        else
        {
            if (!TeamData.ContainsKey(plr.Team))
                TeamData[plr.Team] = new TeamDatas();

            TeamData[plr.Team].ExceMess = exceMess;
        }
    }

    private DateTime GetCoolTime(TSPlayer plr)
    {
        if (!Config.Team)
        {
            if (!CoolTime.ContainsKey(plr.Name))
                CoolTime[plr.Name] = DateTime.UtcNow;

            return CoolTime[plr.Name];
        }
        else
        {
            if (!TeamData.ContainsKey(plr.Team))
                TeamData[plr.Team] = new TeamDatas();

            return TeamData[plr.Team].CoolTime;
        }
    }

    private void SetCoolTime(TSPlayer plr)
    {
        if (!Config.Team)
        {
            CoolTime[plr.Name] = DateTime.UtcNow;
        }
        else
        {
            if (!TeamData.ContainsKey(plr.Team))
                TeamData[plr.Team] = new TeamDatas();

            TeamData[plr.Team].CoolTime = DateTime.UtcNow;
        }
    }

    private string GetTeamName(int teamId)
    {
        switch (teamId)
        {
            case 0:
                return "[c/54D1C2:白队]";
            case 1:
                return "[c/F4626F:红队]";
            case 2:
                return "[c/FCD665:绿队]";
            case 3:
                return "[c/599CDE:蓝队]";
            case 4:
                return "[c/D8F161:黄队]";
            case 5:
                return "[c/E25BC0:粉队]";
            default:
                return "未知队伍";
        }
    }
    #endregion

    #region 清理队伍数据的方法
    private void ClearTeamData(int teamId)
    {
        if (TeamData.ContainsKey(teamId))
        {
            TeamData[teamId].Respawn.Clear();
            TeamData[teamId].Content = "";
            TeamData[teamId].ExceMess = "";

            if (TeamData[teamId].Dead.Count == 0 &&
                TeamData[teamId].Respawn.Count == 0)
            {
                TeamData.Remove(teamId);
            }
        }
    }
    #endregion
}