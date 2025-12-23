using Terraria;
using TShockAPI;
using TShockAPI.Hooks;
using TerrariaApi.Server;
using Microsoft.Xna.Framework;
using System.Text;

namespace DeathEvent;

[ApiVersion(2, 1)]
public class DeathEvent : TerrariaPlugin
{
    #region 插件信息
    public override string Name => "共同死亡事件";
    public override string Author => "Kimi,羽学";
    public override Version Version => new(1, 0, 1);
    public override string Description => "玩家死亡实现共同死亡事件,允许重生后实现补偿";
    #endregion

    #region 注册与释放
    public DeathEvent(Main game) : base(game) { }
    public override void Initialize()
    {
        LoadConfig();
        GeneralHooks.ReloadEvent += ReloadConfig;
        GetDataHandlers.PlayerSpawn += OnSpawn;
        GetDataHandlers.KillMe += OnKillMe;
        ServerApi.Hooks.ServerLeave.Register(this, OnServerLeave);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GeneralHooks.ReloadEvent -= ReloadConfig;
            GetDataHandlers.PlayerSpawn -= OnSpawn;
            GetDataHandlers.KillMe -= OnKillMe;
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

    #region 玩家死亡事件
    public List<string> Players = new List<string>();
    private void OnKillMe(object? sender, GetDataHandlers.KillMeEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];

        // 玩家为空，非真实玩家，插件未启用，或玩家在免疫名单中则返回
        if (plr == null || !plr.RealPlayer ||
            !Config.Enabled || Config is null ||
            Config.WhiteList.Contains(plr.Name)) return;

        e.Handled = true;

        // 只有第一个死亡玩家触发时清空列表
        if (Players.Count == 0)
        {
            TSPlayer.All.SendMessage($"{plr.Name}死亡，正在执行共同死亡事件", 240,250,150);
            MsgList.Clear(); // 清空消息列表
        }

        Players.Add(plr.Name);

        // 检查是否所有玩家均存活
        foreach (var p in TShock.Players)
        {
            if (Players.Contains(p.Name)) continue;

            p.KillPlayer();
            TSPlayer.All.SendData(PacketTypes.DeadPlayer, "", p.Index);
        }
    }
    #endregion

    #region 玩家重生事件-实现死亡补偿
    public List<string> MsgList = new List<string>(); // 存储玩家名字列表
    public string Content = ""; // 存储补偿内容
    private void OnSpawn(object o, GetDataHandlers.SpawnEventArgs e)
    {
        var plr = TShock.Players[e.PlayerId];

        // 玩家为空，非真实玩家，或插件未启用则返回
        if (plr == null || !plr.RealPlayer ||
            !Config.Enabled || Config is null) return;

        // 玩家重生时从名单中移除
        if (Players.Contains(plr.Name))
        {
            var mess = new StringBuilder();
            HandleSpawn(plr, mess); // 执行死亡补偿方法

            // 如果是第一个重生的玩家，记录补偿内容
            if (MsgList.Count == 0)
            {
                Content = mess.ToString();
            }

          
            MsgList.Add(plr.Name);    // 记录玩家名字
            Players.Remove(plr.Name); // 从补偿名单中移除
        }

        // 只有当所有玩家都重生时才播报
        if (Players.Count == 0 && Config.Broadcast && MsgList.Count > 0)
        {
            var names = string.Join("、", MsgList);
            TSPlayer.All.SendMessage($"\n补尝内容：{Content}\n" +
                                     $"补尝名单：{names}", Color.Yellow);

            MsgList.Clear(); // 清空消息列表
            Content = ""; // 清空补偿内容
        }
    }
    #endregion

    #region 玩家离开服务器事件
    private void OnServerLeave(LeaveEventArgs args)
    {
        var plr = TShock.Players[args.Who];

        if (Players.Contains(plr.Name))
        {
            Players.Remove(plr.Name);
        }
    } 
    #endregion

    #region 共同死亡补偿方法
    private static void HandleSpawn(TSPlayer plr, StringBuilder mess)
    {
        var tplr = plr.TPlayer;

        // 死亡增加生命
        if (Config.AddLifeAmount > 0)
        {
            tplr.statLife = tplr.statLifeMax += Config.AddLifeAmount;
            plr.SendData(PacketTypes.PlayerHp, "", plr.Index);
            plr.SendData(PacketTypes.PlayerInfo, "", plr.Index);
            mess.Append($"\n生命上限 +{Config.AddLifeAmount},");  // 记录消息
        }

        // 死亡增加魔力
        if (Config.AddManaAmount > 0)
        {
            tplr.statMana = tplr.statManaMax += Config.AddManaAmount;
            plr.SendData(PacketTypes.PlayerMana, "", plr.Index);
            plr.SendData(PacketTypes.PlayerInfo, "", plr.Index);
            mess.Append($"\n魔力上限 +{Config.AddLifeAmount},");  // 记录消息
        }

        // 执行命令表不为空
        if (Config.DeathCommands is not null)
        {
            mess.Append($"\n执行命令: ");  // 记录消息
            Group group = plr.Group;

            try
            {
                plr.Group = new SuperAdminGroup();
                foreach (var cmd in Config.DeathCommands)
                {
                    Commands.HandleCommand(plr, cmd);
                    mess.Append($"\n{cmd}");  // 记录消息
                }
            }
            finally
            {
                plr.Group = group;
            }
        }

        // 如果给予物品选项开启且物品表不为空
        if (Config.GiveItem && Config.ItemList is not null)
        {
            // 给予列表里的物品
            mess.Append("\n获得物品: ");
            foreach (var item in Config.ItemList)
            {
                int itemType = item.Key;
                int itemStack = item.Value;
                plr.GiveItem(itemType, itemStack);
                mess.Append($"[i/s{itemStack}:{itemType}] ");  // 记录消息
            }
        }
    }
    #endregion

}