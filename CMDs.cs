using System.Text;
using TShockAPI;
using Terraria;
using static DeathEvent.DeathEvent;
using static DeathEvent.Tool;
using Microsoft.Xna.Framework;

namespace DeathEvent;

internal class CMDs
{
    #region 主指令方法
    public static readonly string Admin = "det.admin";
    internal static void CMD(CommandArgs args)
    {
        if (!Config.Enabled) return;
        var plr = args.Player;

        //子命令数量为0时显示帮助
        if (args.Parameters.Count == 0)
        {
            HelpCmd(plr);
            return;
        }

        // 处理子命令
        switch (args.Parameters[0].ToLower())
        {
            case "on":
                SwitchPlugs(plr, true);
                break;

            case "off":
                SwitchPlugs(plr, false);
                break;

            case "y":
            case "yes":
            case "是":
            case "允许":
            case "同意":
                Vote.Action(plr,true);
                break;

            case "n":
            case "no":
            case "不":
            case "拒绝":
            case "不同意":
                Vote.Action(plr, false);
                break;

            case "vote":
            case "v":
                ShowVoteCmd(plr);
                break;

            case "s":
            case "set":
                SetCmd(plr, args);
                break;

            case "ck":
                CheckCmd(plr, args);
                break;

            case "wl":
                WlCmd(plr, args);
                break;

            case "item":
                ItemCmd(plr);
                break;

            case "cmd":
                CmdCmd(plr, args);
                break;

            case "rst":
                RstCmd(plr, args);
                break;

            case "evt":
                EvtCmd(plr);
                break;

            default:
                HelpCmd(plr);
                break;
        }
    }
    #endregion

    #region 开启关闭插件方法
    private static void SwitchPlugs(TSPlayer plr, bool enable)
    {
        if (!plr.HasPermission(Admin)) return;
        Config.Enabled = enable;
        Config.Write();
        plr.SendMessage($"已{(enable ? "开启" : "关闭")}插件功能", color);
    }
    #endregion

    #region 查看投票状态的方法
    private static void ShowVoteCmd(TSPlayer plr)
    {
        if (!Config.Team)
        {
            plr.SendMessage("当前为非队伍模式，无投票功能", color);
            return;
        }

        if (!Config.TeamApply)
        {
            plr.SendMessage("队伍申请功能未开启", color);
            return;
        }

        // 查找玩家当前队伍的投票
        var vote = Vote.VoteData.Values.FirstOrDefault(v =>
            v.Team == plr.Team && !v.IsEnd && plr.Name != v.AppName);

        if (vote == null)
        {
            plr.SendMessage("当前没有进行中的投票", color);
            return;
        }

        Vote.ShowStatus(plr, vote);
    } 
    #endregion

    #region 查询数据方法
    private static void CheckCmd(TSPlayer plr, CommandArgs args)
    {
        if (args.Parameters.Count < 2)
        {
            plr.SendMessage("用法: /det ck <玩家名|队伍名>", color);
            return;
        }

        string name = args.Parameters[1];
        string msg;

        // 检查玩家
        var pData = Cache.GetData(name);
        if (pData != null && pData.DeathCount > 0)
        {
            msg = $"{name} 死亡次数: {pData.DeathCount}次";
            if (pData.SwitchTime.HasValue)
            {
                TimeSpan cd = DateTime.Now - pData.SwitchTime.Value;
                double remain = Config.SwitchCD - cd.TotalSeconds;
                if (remain > 0)
                    msg += $", 切换CD: {remain:F1}秒";
            }
            plr.SendMessage(msg, color);
            return;
        }

        // 检查队伍
        int count = Cache.GetTeamDeath(name);
        if (count > 0)
        {
            plr.SendMessage($"{name}队伍 死亡次数: {count}次", color);
            return;
        }

        plr.SendMessage($"未找到 {name} 的数据", Color.Yellow);
    }
    #endregion

    #region 物品查询方法
    private static void ItemCmd(TSPlayer plr)
    {
        if (Config.ItemList.Count == 0)
        {
            plr.SendMessage("补尝物品表为空", color);
            return;
        }

        var mess = new StringBuilder();
        mess.AppendLine("补尝物品表:");

        foreach (var item in Config.ItemList)
        {
            int type = item.Key;
            int Stack = item.Value;
            if (plr.RealPlayer)
                mess.AppendLine($"{ItemIconStack(type, Stack)} ");
            else
                mess.AppendLine($"{Lang.GetItemNameValue(type)} x{Stack}");
        }

        plr.SendMessage(mess.ToString(), color);
        // 提示使用set指令进行增删
        if (plr.HasPermission(Admin))
        {
            plr.SendMessage("修改请使用: /det s item 物名 数量", color);
            plr.SendMessage("只输item 不带物品名,切换补尝物品开关", color);
            plr.SendMessage("只输物品名,存在则移除,不在则添加", color);
            plr.SendMessage("带数量,存在则修改数量,不在则按数量添加", color);
            plr.SendMessage("注:物品名也可以是物品id,电脑玩家支持:Alt+鼠标左键选择物品图标", color);
            plr.SendMessage("使用/det s item -i 则获取手上选择的物品及数量来决定添加或移除", color);
        }
    }
    #endregion

    #region 补偿指令查询方法
    private static void CmdCmd(TSPlayer plr, CommandArgs args)
    {
        if (Config.DeathCommands.Length == 0)
        {
            plr.SendMessage("补尝执行命令为空", color);
            return;
        }

        var mess = new StringBuilder();
        mess.AppendLine("补尝执行命令:");

        foreach (var cmd in Config.DeathCommands)
        {
            mess.AppendLine($"{cmd}");
        }

        plr.SendMessage(mess.ToString(), color);
        // 提示使用set指令进行增删
        if (plr.HasPermission(Admin))
        {
            plr.SendMessage("修改请使用: /det s cmd 命令名", color);
            plr.SendMessage("存在则移除,不在则添加,无需输/", color);
            plr.SendMessage("例如给6分钟羽落:/det s cmd buff 8 360", color);
        }
    }
    #endregion

    #region 白名单查询方法
    private static void WlCmd(TSPlayer plr, CommandArgs args)
    {
        // 只显示白名单，增删功能已移到set指令
        if (Config.WhiteList.Count == 0)
        {
            plr.SendMessage("白名单为空", color);
        }
        else
        {
            string list = string.Join("、 ", Config.WhiteList);
            plr.SendMessage($"白名单: {list}", color);
        }

        // 提示使用set指令进行增删
        if (plr.HasPermission(Admin))
        {
            plr.SendMessage("修改请使用: /det s wl <玩家名>", color);
            plr.SendMessage("存在则移除,不在则添加", color);
        }
    }
    #endregion

    #region 死亡事件查询方法
    private static void EvtCmd(TSPlayer plr)
    {
        if (!Config.Team)
        {
            plr.SendMessage("当前为非队伍模式", color);
            return;
        }

        string msg = "当前死亡事件:\n";
        bool hasEvent = false;

        foreach (var kv in Data.TeamData)
        {
            if (kv.Value.Dead.Count > 0)
            {
                hasEvent = true;
                string teamName = Data.GetTeamName(kv.Key);
                msg += $"{teamName}: {kv.Value.Dead.Count}人死亡\n";
                msg += $"名单: {string.Join(", ", kv.Value.Dead)}\n";
            }
        }

        if (!hasEvent)
        {
            msg += "暂无进行中的死亡事件\n";
        }

        plr.SendMessage(msg, color);
    }
    #endregion

    #region 配置项修改方法
    private static void SetCmd(TSPlayer plr, CommandArgs args)
    {
        if (!plr.HasPermission(Admin)) return;

        if (args.Parameters.Count < 2)
        {
            var mess = new StringBuilder();
            mess.Append("\n用法:\n");
            mess.Append("/det s tm - 队伍模式开关\n");
            mess.Append("/det s rew - 队伍激励开关\n");
            mess.Append("/det s app - 队伍申请开关\n");
            mess.Append("/det s sync - 同步最高生命者开关\n");
            mess.Append("/det s exce - 超出服务器上限开关\n");
            mess.Append("/det s item - 重生补尝物品开关\n");
            mess.Append("/det s item 物名 - 修改补尝物品\n");
            mess.Append("/det s item 物名 数量 - 设置物品数量\n");
            mess.Append("/det s cmd 命令 - 修改补尝命令\n");
            mess.Append("/det s wl 玩家名 - 修改免疫名单\n");
            mess.Append("/det s life 数值 - 设置补尝生命\n");
            mess.Append("/det s mana 数值 - 设置补尝魔力\n");
            mess.Append("/det s time 秒 - 设置复活时间\n");
            mess.Append("/det s cool 秒 - 设置补尝冷却\n");
            mess.Append("/det s cd 秒 - 设置队伍切换CD\n");
            mess.Append("/det s vt 秒 - 设置投票时间\n");

            mess.Append("注:'修改'的意思是存在则移除,不在则添加\n");

            if (plr.RealPlayer)
            {
                plr.SendMessage(TextGradient(mess.ToString()), color);
            }
            else
            {
                plr.SendMessage(mess.ToString(), color);
            }

            return;
        }

        string key = args.Parameters[1].ToLower();
        string msg = "";

        switch (key)
        {
            case "tm":
            case "team":
                Config.Team = !Config.Team;
                msg = $"队伍模式: {(Config.Team ? "开" : "关")}";
                break;

            case "app":
            case "apply":
                Config.TeamApply = !Config.TeamApply;
                msg = $"队伍申请: {(Config.TeamApply ? "开" : "关")}";
                break;

            case "sync":
                Config.SyncLifeMax = !Config.SyncLifeMax;
                msg = $"同步最高生命者: {(Config.SyncLifeMax ? "开" : "关")}";
                break;

            case "exce":
                Config.ExceMax = !Config.ExceMax;
                msg = $"超出服务器上限: {(Config.ExceMax ? "开" : "关")}";
                break;

            case "rew":
                Config.Incentive = !Config.Incentive;
                msg = $"队伍激励: {(Config.Incentive ? "开" : "关")}";
                break;

            case "lf":
            case "life":
                if (args.Parameters.Count < 3)
                {
                    plr.SendMessage("用法: /det set life <数值>", color);
                    return;
                }
                if (int.TryParse(args.Parameters[2], out int life))
                {
                    Config.AddLifeAmount = life;
                    msg = $"补尝生命: {life}";
                }
                break;

            case "ma":
            case "mana":
                if (args.Parameters.Count < 3)
                {
                    plr.SendMessage("用法: /det set mana <数值>", color);
                    return;
                }
                if (int.TryParse(args.Parameters[2], out int mana))
                {
                    Config.AddManaAmount = mana;
                    msg = $"补尝魔力: {mana}";
                }
                break;

            case "t":
            case "time":
                if (args.Parameters.Count < 3)
                {
                    plr.SendMessage("用法: /det set time <秒数>", color);
                    return;
                }
                if (int.TryParse(args.Parameters[2], out int time))
                {
                    Config.RespawnTimer = time;
                    msg = $"复活时间: {time}秒";
                }
                break;

            case "cl":
            case "cool":
                if (args.Parameters.Count < 3)
                {
                    plr.SendMessage("用法: /det set cool <秒数>", color);
                    return;
                }
                if (int.TryParse(args.Parameters[2], out int cool))
                {
                    Config.CoolDowned = cool;
                    msg = $"补尝冷却: {cool}秒";
                }
                break;

            case "cd":
                if (args.Parameters.Count < 3)
                {
                    plr.SendMessage("用法: /det set cd <秒数>", color);
                    return;
                }
                if (int.TryParse(args.Parameters[2], out int cd))
                {
                    Config.SwitchCD = cd;
                    msg = $"切换CD: {cd}秒";
                }
                break;

            case "vt":
            case "vote":
                if (args.Parameters.Count < 3)
                {
                    plr.SendMessage("用法: /det set vt <秒数>", color);
                    return;
                }
                if (int.TryParse(args.Parameters[2], out int vt))
                {
                    Config.VoteTime = vt;
                    msg = $"投票时间: {vt}秒";
                }
                break;

            case "wl":
                if (args.Parameters.Count < 3)
                {
                    plr.SendMessage("用法: /det s wl <玩家名>", color);
                    plr.SendMessage("存在则移除,不在则添加", color);
                    return;
                }

                string pName = args.Parameters[2];
                if (Config.WhiteList.Contains(pName))
                {
                    // 存在则移除
                    Config.WhiteList.Remove(pName);
                    if (plr.RealPlayer)
                        msg = $"已从白名单[c/F5636F:移除] [{pName}]";
                    else
                        msg = $"已从白名单移除 {pName}";
                }
                else
                {
                    // 不存在则添加
                    Config.WhiteList.Add(pName);
                    if (plr.RealPlayer)
                        msg = $"[c/508DC8:已添加]到白名单 [{pName}]";
                    else
                        msg = $"已添加 [{pName}] 到白名单";
                }
                break;

            case "cmd":
                if (args.Parameters.Count < 3)
                {
                    plr.SendMessage("用法: /det s cmd <命令名>", color);
                    plr.SendMessage("存在则移除,不在则添加,<命令名>无需写/", color);
                    plr.SendMessage("例如给6分钟羽落:/det s cmd buff 8 360", color);
                    return;
                }

                // 支持多个单词的命令（如 "buff 8 360"）
                string cmdName = string.Join(" ", args.Parameters.Skip(2));
                ProcCmd(plr, cmdName);
                return;

            case "item":
                if (args.Parameters.Count == 2)
                {
                    Config.GiveItem = !Config.GiveItem;
                    msg = $"补尝物品: {(Config.GiveItem ? "开" : "关")}";
                }
                else if (args.Parameters.Count == 3)
                {
                    // 检查是否为 -i 参数
                    if (args.Parameters[2].ToLower() == "-i")
                    {
                        // 根据玩家选择物品操作
                        ProcItemBySelection(plr);
                        return;
                    }
                    else
                    {
                        ProcItem(plr, args.Parameters[2]);
                        return;
                    }
                }
                else if (args.Parameters.Count == 4)
                {
                    if (!int.TryParse(args.Parameters[3], out int amount) || amount <= 0)
                    {
                        plr.SendMessage("数量必须为正整数", Color.Yellow);
                        return;
                    }

                    ProcItem(plr, args.Parameters[2], amount);
                    return;
                }
                else
                {
                    plr.SendMessage("用法: /det s item [物品名] [数量]", color);
                    plr.SendMessage("只输item 不带物品名,切换补尝物品开关", color);
                    plr.SendMessage("只输物品名,存在则移除,不在则添加", color);
                    plr.SendMessage("带数量,存在则修改数量,不在则按数量添加", color);
                    plr.SendMessage("注:物品名也可以是物品id,电脑玩家支持:Alt+鼠标左键选择物品图标", color);
                    plr.SendMessage("使用/det s item -i 则获取手上选择的物品及数量来决定添加或移除", color);
                    return;
                }
                break;

            default:
                plr.SendMessage("未知配置项", color);
                return;
        }

        if (!string.IsNullOrEmpty(msg))
        {
            Config.Write();
            plr.SendMessage($"{msg}", color);
        }
        else
        {
            plr.SendMessage("操作失败", Color.Yellow);
        }
    }
    #endregion

    #region 修改补偿物品方法
    private static Item? GetItem(TSPlayer plr, string itemName)
    {
        var items = TShock.Utils.GetItemByIdOrName(itemName);

        if (items.Count == 0)
        {
            plr.SendErrorMessage($"未找到物品: {itemName}");
            return null;
        }

        if (items.Count > 1)
        {
            plr.SendMultipleMatchError(items.Select(i => $"{i.Name}(ID:{i.netID})"));
            return null;
        }

        return items[0];
    }

    private static void ProcItem(TSPlayer plr, string itemName, int? amount = null)
    {
        var item = GetItem(plr, itemName);
        if (item == null) return;

        int itemId = item.netID;
        string msg;

        if (amount.HasValue)
        {
            // 有指定数量：添加/修改
            Config.ItemList[itemId] = amount.Value;
            if (plr.RealPlayer)
                msg = $"已{(Config.ItemList.ContainsKey(itemId) ? "修改" : "添加")}补尝物品: {ItemIconStack(itemId, amount.Value)}";
            else
                msg = $"已{(Config.ItemList.ContainsKey(itemId) ? "修改" : "添加")}补偿物品: {Lang.GetItemNameValue(itemId)} x{amount.Value}";
        }
        else
        {
            // 无指定数量：切换（存在就移除，不存在就添加,默认1）
            if (Config.ItemList.ContainsKey(itemId))
            {
                Config.ItemList.Remove(itemId);
                if (plr.RealPlayer)
                    msg = $"[c/F5636F:已移除]补尝物品: {ItemIcon(itemId)}";
                else
                    msg = $"已移除补偿物品: {Lang.GetItemNameValue(itemId)}";
            }
            else
            {
                Config.ItemList[itemId] = 1;
                if (plr.RealPlayer)
                    msg = $"[c/508DC8:已添加]补尝物品: {ItemIconStack(itemId, 1)}";
                else
                    msg = $"已添加补偿物品: {Lang.GetItemNameValue(itemId)} x1";
            }
        }

        Config.Write();
        plr.SendMessage($"{msg}", color);
    }
    #endregion

    #region 根据玩家选择物品修改方法
    private static void ProcItemBySelection(TSPlayer plr)
    {
        // 检查是否为真人玩家
        if (!plr.RealPlayer)
        {
            plr.SendMessage("控制台不能使用 -i 参数", Color.Yellow);
            return;
        }

        // 获取玩家当前手持物品
        var Sel = plr.SelectedItem;

        // 检查物品是否有效
        if (Sel == null || Sel.type <= 0 || Sel.stack <= 0)
        {
            plr.SendMessage("请先选择有效的物品", Color.Yellow);
            plr.SendMessage("提示:手持物品或Alt+鼠标左键选择物品图标", color);
            return;
        }

        int itemId = Sel.type;
        string itemName = Sel.Name;
        int itemStack = Sel.stack;
        string msg;

        // 检查是否已存在
        if (Config.ItemList.ContainsKey(itemId))
        {
            // 存在则移除
            Config.ItemList.Remove(itemId);
            msg = $"[c/F5636F:已移除]补尝物品: {ItemIcon(itemId)}";
        }
        else
        {
            // 不存在则添加
            Config.ItemList[itemId] = itemStack;
            msg = $"[c/508DC8:已添加]补尝物品: {ItemIconStack(itemId, itemStack)}";
        }

        Config.Write();
        plr.SendMessage($"{msg}", color);
    }
    #endregion

    #region 修改补偿指令方法
    private static void ProcCmd(TSPlayer plr, string cmd)
    {
        // 自动添加斜杠前缀
        if (!cmd.StartsWith("/"))
        {
            cmd = "/" + cmd;
        }

        string msg;

        // 检查是否已存在（不区分大小写）
        var has = Config.DeathCommands.FirstOrDefault(c => string.Equals(c, cmd, StringComparison.OrdinalIgnoreCase));
        if (has != null)
        {
            // 移除命令
            Config.DeathCommands = Config.DeathCommands.Where(c => !string.Equals(c, cmd, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (plr.RealPlayer)
                msg = $"[c/F5636F:已移除]补尝命令: {cmd}";
            else
                msg = $"已移除补偿命令: {cmd}";
        }
        else
        {
            // 添加命令
            var newList = Config.DeathCommands.ToList();
            newList.Add(cmd);
            Config.DeathCommands = newList.ToArray();
            if (plr.RealPlayer)
                msg = $"[c/508DC8:已添加]补尝命令: {cmd}";
            else
                msg = $"已添加补偿命令: {cmd}";
        }

        Config.Write();
        plr.SendMessage($"{msg}", color);
    }
    #endregion

    #region 重置数据方法
    private static void RstCmd(TSPlayer plr, CommandArgs args)
    {
        if (!plr.HasPermission(Admin)) return;

        if (args.Parameters.Count < 2)
        {
            plr.SendMessage("用法: /det rst <玩家名|队伍名|all>", color);
            return;
        }

        string name = args.Parameters[1].ToLower();

        // 重置玩家和队伍数据
        if (name == "all")
        {
            Vote.ClearAll();
            Data.TeamData.Clear();
            Cache.PlayerData.Clear();
            Cache.TeamDeathCount.Clear();
            Cache.Write();
            plr.SendMessage("已重置所有玩家和队伍的死亡次数", color);
            return;
        }

        // 重置玩家
        if (Cache.PlayerData.ContainsKey(name))
        {
            Cache.PlayerData[name].DeathCount = 0;
            Cache.Write();
            plr.SendMessage($"已重置 {name} 的死亡次数", color);
            return;
        }

        // 重置队伍
        if (Cache.TeamDeathCount.ContainsKey(name))
        {
            Cache.TeamDeathCount[name] = 0;
            Cache.Write();
            plr.SendMessage($"已重置 {name} 队伍的死亡次数", color);
            return;
        }

        plr.SendMessage($"未找到 {name} 的数据", Color.Yellow);
    }
    #endregion

    #region 菜单方法
    private static void HelpCmd(TSPlayer plr)
    {
        var mess = new StringBuilder();

        // 构建消息内容
        if (plr.RealPlayer)
        {
            plr.SendMessage("[i:3455][c/AD89D5:共][c/D68ACA:同][c/DF909A:死][c/E5A894:亡][i:3454] " +
            "[i:3456][C/F2F2C7:开发] [C/BFDFEA:by] [c/00FFFF:羽学 | Kimi] [i:3459]", color);

            // 管理员版本
            if (plr.HasPermission(Admin))
            {
                mess.Append("/det on|off - 开关插件\n");
                mess.Append("/det y|n - 投票同意/拒绝\n");
                mess.Append("/det v - 查看投票状态\n");
                mess.Append("/det s - 修改配置功能\n");
                mess.Append("/det ck - 查数据\n");
                mess.Append("/det wl - 查白名单\n");
                mess.Append("/det item - 查补尝物品\n");
                mess.Append("/det cmd - 查补尝命令\n");
                mess.Append("/det rst 玩家名|队伍名 - 重置次数\n");
                mess.Append("/det evt - 查死亡事件");
            }
            else // 普通玩家版本
            {
                mess.Append("/det y|n - 投票同意/拒绝\n");
                mess.Append("/det v - 查看投票状态\n");
                mess.Append("/det ck - 查数据\n");
                mess.Append("/det wl - 查白名单\n");
                mess.Append("/det item - 查补尝物品\n");
                mess.Append("/det cmd - 查补尝命令\n");
                mess.Append("/det evt - 查死亡事件");
            }

            plr.SendMessage(TextGradient(mess.ToString()), color);
        }
        else
        {
            // 控制台版本
            mess.AppendLine("《共同死亡》指令:");
            mess.AppendLine("/det on|off - 开关插件");
            mess.AppendLine("/det s - 修改配置功能");
            mess.AppendLine("/det ck - 查数据");
            mess.AppendLine("/det wl - 查白名单");
            mess.AppendLine("/det item - 查补尝物品");
            mess.AppendLine("/det cmd - 查补尝命令");
            mess.AppendLine("/det rst 玩家名|队伍名 - 重置次数");
            mess.AppendLine("/det evt - 查死亡事件");

            plr.SendMessage(mess.ToString(), color);
        }
    }
    #endregion
}