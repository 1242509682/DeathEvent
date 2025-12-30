using Terraria;
using TShockAPI;
using System.Text;
using TShockAPI.Hooks;
using TerrariaApi.Server;
using Microsoft.Xna.Framework;
using static DeathEvent.Data;

namespace DeathEvent;

[ApiVersion(2, 1)]
public class DeathEvent : TerrariaPlugin
{
    #region 插件信息
    public override string Name => "共同死亡事件";
    public override string Author => "Kimi,羽学";
    public override Version Version => new(1, 0, 3);
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

    #region 玩家死亡事件
    private void OnKillMe(object? sender, GetDataHandlers.KillMeEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];

        if (plr is null || !plr.RealPlayer ||
            !Config.Enabled || Config is null) return;

        var dead = GetDead(plr);
        if (dead.Count == 0)
        {
            // 新的死亡事件开始，清理上次的数据
            ClearData(plr);

            // 更新死亡统计
            Config.AddDeath(plr.Name);
            if (Config.Team && plr.Team > -1)
            {
                // 现在只在这里增加队伍死亡次数
                Config.AddTeamDeath(plr.Team);
            }

            string msg;
            if (Config.Team)
            {
                msg = $"————[c/508DC8:{plr.Name}]死亡————" +
                      $"\n{GetTeamName(plr.Team)}死亡{Config.GetTeamDeath(plr.Team)}次，" +
                      $"正在执行队伍死亡事件";
            }
            else
            {
                msg = $"————[c/508DC8:{plr.Name}]死亡————" +
                      $"\n个人死亡{Config.GetDeath(plr.Name)}次，" +
                      $"正在执行共同死亡事件";
            }

            TSPlayer.All.SendMessage(msg, 240, 250, 150);
            GetResp(plr).Clear();
        }
        else
        {
            // 后续死亡的玩家只增加个人死亡次数
            Config.AddDeath(plr.Name);
        }

        if (dead.Contains(plr.Name)) return;

        dead.Add(plr.Name);
        plr.RespawnTimer = Config.RespawnTimer;

        int team = Config.Team ? plr.Team : -1;
        for (int i = 0; i < TShock.Players.Length; i++)
        {
            var p = TShock.Players[i];
            if (p == null || !p.RealPlayer || !p.Active || p.Dead) continue;
            if (dead.Contains(p.Name)) continue;
            if (Config.WhiteList.Contains(p.Name)) continue;
            if (Config.Team && p.Team != team) continue;

            p.KillPlayer();
            TSPlayer.All.SendData(PacketTypes.DeadPlayer, "", p.Index);
        }
    }
    #endregion

    #region 玩家重生补偿事件
    private void OnSpawn(object o, GetDataHandlers.SpawnEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];
        if (plr == null || !plr.RealPlayer || !Config.Enabled) return;

        // 显示死亡统计
        if (Config.Team && plr.Team > -1)
        {
            plr.SendMessage($"{GetTeamName(plr.Team)}总死亡次数: [c/508DC8:{Config.GetTeamDeath(plr.Team)}]次", Color.LightGreen);
        }
        else
        {
            plr.SendMessage($"个人死亡次数: [c/508DC8:{Config.GetDeath(plr.Name)}]次", Color.LightGreen);
        }

        SyncLife(plr);

        var dead = GetDead(plr);
        var resp = GetResp(plr);

        if (!dead.Contains(plr.Name)) return;

        DateTime coolT = GetCoolDown(plr);
        TimeSpan timeSpan = DateTime.UtcNow - coolT;
        double remain = Config.CoolDowned - timeSpan.TotalSeconds;
        if (remain > 0)
        {
            string teamName = GetTeamName(plr.Team);
            string coolMsg = Config.Team ? $"{teamName}补尝冷却中，剩余: [c/508DC8:{remain:f2}]秒"
                                         : $"补尝冷却中，剩余: [c/508DC8:{remain:f2}]秒";

            plr.SendMessage(coolMsg, Color.White);
            resp.Add(plr.Name);
            dead.Remove(plr.Name);
            return;
        }

        var mess = new StringBuilder();
        HandleSpawn(plr, mess);

        string cont = mess.ToString();
        string exc = GetExc(plr);

        if (!string.IsNullOrEmpty(exc)) 
            cont += exc;

        // 检查是否是第一个重生的玩家
        if (resp.Count == 0)
        {
            // 第一个重生的玩家，设置补偿内容
            SetCont(plr, cont);
        }
        else
        {
            // 不是第一个重生的玩家，获取已记录的补偿内容
            string savedCont = GetCont(plr);
            if (!string.IsNullOrEmpty(savedCont))
            {
                cont = savedCont; // 使用已保存的补偿内容
            }
            else
            {
                // 如果没有保存的内容，则保存当前内容
                SetCont(plr, cont);
            }
        }

        resp.Add(plr.Name);
        dead.Remove(plr.Name);

        // 广播逻辑：当所有玩家都重生时（dead.Count == 0）
        if (dead.Count == 0 && resp.Count > 0)
        {
            // 重新获取补偿内容，确保正确
            string finalCont = GetCont(plr);
            if (string.IsNullOrEmpty(finalCont))
            {
                // 如果没有补偿内容，则使用当前构建的内容
                finalCont = cont;
            }

            string names = string.Join("、", resp);
            string prefix = Config.Team ? $"{GetTeamName(plr.Team)}" : "";

            string text;
            if (Config.Team)
            {
                text = $"\n{prefix}死亡事件补尝(总死亡{Config.GetTeamDeath(plr.Team)}次)：{finalCont}\n队伍名单：{names}";
            }
            else
            {
                text = $"\n共同死亡事件补尝：{finalCont}\n补尝名单：{names}";
            }

            if (Config.Team)
            {
                int team = plr.Team;
                for (int i = 0; i < TShock.Players.Length; i++)
                {
                    var p = TShock.Players[i];
                    if (p != null && p.Team == team)
                    {
                        Tool.GradMess(p,text); // 逐行渐变
                    }
                }
            }
            else
            {
                TSPlayer.All.SendMessage(Tool.TextGradient(text), Color.White); // 逐字渐变
            }

            SetCoolDown(plr);
            HandleExce(plr);
            ClearData(plr);
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

            if (!Config.ExceMax && tplr.statLifeMax > TShock.Config.Settings.MaxHP)
                tplr.statLife = tplr.statLifeMax = TShock.Config.Settings.MaxHP;

            plr.SendData(PacketTypes.PlayerHp, null, plr.Index);
            mess.Append($"\n生命+{Config.AddLifeAmount},");
        }

        if (Config.AddManaAmount > 0)
        {
            tplr.statMana = tplr.statManaMax += Config.AddManaAmount;

            if (!Config.ExceMax && tplr.statManaMax > TShock.Config.Settings.MaxMP)
                tplr.statMana = tplr.statManaMax = TShock.Config.Settings.MaxMP;

            plr.SendData(PacketTypes.PlayerMana, null, plr.Index);
            mess.Append($"\n魔力+{Config.AddManaAmount},");
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
    }
    #endregion

    #region 同步最大生命值的方法
    private void SyncLife(TSPlayer plr)
    {
        if (!Config.SyncLifeMax) return;

        int maxLife = 0;
        TSPlayer? maxPlr = null;

        for (int i = 0; i < TShock.Players.Length; i++)
        {
            var p = TShock.Players[i];
            if (p == null || !p.RealPlayer || !p.Active) continue;
            if (Config.WhiteList.Contains(p.Name)) continue;
            if (Config.Team && p.Team != plr.Team) continue;
            if (p.Index == plr.Index) continue;

            int curr = p.TPlayer.statLifeMax;
            if (curr > maxLife)
            {
                maxLife = curr;
                maxPlr = p;
            }
        }

        if (maxPlr != null && plr.TPlayer.statLifeMax < maxLife)
        {
            plr.TPlayer.statLifeMax = maxLife;
            plr.SendData(PacketTypes.PlayerHp, null, plr.Index);

            string msg = Config.Team
                ? $"已将您最大生命值,同步至当前{GetTeamName(plr.Team)}内数值最高玩家:[c/508DC8:{maxPlr.Name}]"
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

                Task.Run(() => {
                    TShock.Config.Write(Path.Combine(TShock.SavePath, "config.json"));
                });

                string exc = GetExc(plr);
                string newExc = $"\n已提升上限: {info}";

                if (string.IsNullOrEmpty(exc))
                    SetExc(plr, newExc);
                else if (!exc.Contains(info.ToString()))
                    SetExc(plr, exc + $", {info}");
            }
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
            if (CheckSwitch(plr.Name) && !Config.WhiteList.Contains(plr.Name))
            {
                var coolT = GetSwitch(plr.Name);
                TimeSpan timeSpan = DateTime.UtcNow - coolT;
                double remain = Config.SwitchCD - timeSpan.TotalSeconds;
                plr.SendMessage($"队伍切换冷却中，请等待:[c/508DC8:{remain:f2}]秒", 240, 250, 150);
                e.Handled = true;
                plr.SetTeam(oldTeam); // 强制还原队伍
                return;
            }

            // 保存队伍信息
            string teamName = Config.GetTeamName(newTeam);
            Config.BackTeam[plr.Name] = teamName;
            Config.Write();

            // 如果不是白名单玩家，设置切换队伍冷却
            if (!Config.WhiteList.Contains(plr.Name))
            {
                SetSwitch(plr.Name);
            }

            var oldData = GetData(oldTeam);
            oldData.Dead.Remove(plr.Name);
            oldData.Resp.Remove(plr.Name);
            ClearData(plr);

            string oldName = GetTeamName(oldTeam);
            string newName = GetTeamName(newTeam);
            plr.SendMessage($"您已从{oldName}切换到{newName}", 240, 250, 150);

            var newData = GetData(newTeam);
            if (newData.Dead.Count > 0)
            {
                plr.SendMessage($"注意：{newName}已有{newData.Dead.Count}名队员死亡", 240, 250, 150);
            }

            TimeSpan timeSpan2 = DateTime.UtcNow - newData.CoolDown;
            double remain2 = Config.CoolDowned - timeSpan2.TotalSeconds;
            if (remain2 > 0)
            {
                plr.SendMessage($"{newName}补尝冷却中，剩余: [c/508DC8:{remain2:f2}]秒", 240, 250, 150);
            }
        }
        else
        {
            var data = GetData(-1);
            if (data.Dead.Contains(plr.Name))
            {
                data.Dead.Remove(plr.Name);
                ClearData(plr);
            }
        }
    }
    #endregion

    #region 玩家进入服务器
    private void OnGreetPlayer(GreetPlayerEventArgs args)
    {
        var plr = TShock.Players[args.Who];
        if (plr == null || !plr.RealPlayer ||
            Config is null || !Config.Team) return;

        // 恢复队伍
        if (Config.BackTeam.ContainsKey(plr.Name))
        {
            string teamName = Config.BackTeam[plr.Name];
            int teamId = Config.GetTeamByName(teamName);

            if (teamId != plr.Team && teamId > 0)
            {
                plr.SetTeam(teamId);
                plr.SendMessage($"已恢复您的队伍为{GetTeamName(teamId)}", 240, 250, 150);
            }
        }
    }
    #endregion

    #region 玩家离开服务器事件
    private void OnServerLeave(LeaveEventArgs args)
    {
        var plr = TShock.Players[args.Who];
        if (plr == null) return;

        int teamId = Config.Team ? plr.Team : -1;
        if (MyData.ContainsKey(teamId))
        {
            var data = MyData[teamId];
            data.Dead.Remove(plr.Name);
            data.Resp.Remove(plr.Name);
            ClearData(plr);
        }
    }
    #endregion

}