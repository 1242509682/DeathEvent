using TShockAPI;
using Terraria;
using static DeathEvent.Data;
using static DeathEvent.DeathEvent;

namespace DeathEvent;

internal class Rewards
{
    #region 激励功能相关
    public static Dictionary<string, List<Tuple<int, int, int>>> TeamSlot = new Dictionary<string, List<Tuple<int, int, int>>>();
    public static Dictionary<int, bool> TeamRew = new Dictionary<int, bool>();
    public static void DoReward(TSPlayer plr, HashSet<int> other)
    {
        // 检查激励功能是否开启
        if (!Config.Team || !Config.Incentive || other.Count == 0) return;

        int team = plr.Team;
        string key = team.ToString();

        // 检查是否已经执行过激励
        if (TeamRew.ContainsKey(team) && TeamRew[team])
            return;

        TeamRew[team] = true;

        // 清空队伍槽位记录
        if (TeamSlot.ContainsKey(key))
            TeamSlot[key].Clear();
        else
            TeamSlot[key] = new List<Tuple<int, int, int>>();

        // 收集同队伍所有玩家的所有槽位物品
        for (int i = 0; i < TShock.Players.Length; i++)
        {
            var p = TShock.Players[i];
            if (p == null || !p.RealPlayer || !p.Active) continue;
            if (p.Team != team) continue;
            if (Config.WhiteList.Contains(p.Name)) continue;

            // 遍历所有槽位
            CollectSlots(p, key);
        }

        var allSlots = TeamSlot[key];
        if (allSlots.Count == 0) return;

        // 随机抽取一个物品槽位
        var rand = Main.rand;
        int rndSlot = rand.Next(allSlots.Count);
        var selSlot = allSlots[rndSlot];

        int id = selSlot.Item1;  // 物品ID
        int who = selSlot.Item2;  // 玩家索引
        int slot = selSlot.Item3;  // 槽位索引

        var fromPlr = TShock.Players[who];  // 物品来源玩家
        if (fromPlr != null && fromPlr.Active && fromPlr.RealPlayer)
        {
            // 从源玩家移除该物品并获取数量
            int stack = RemoveSlot(fromPlr, slot);

            if (stack <= 0) return;

            // 随机选一个其他队伍玩家给予奖励
            int rndPlr = other.ElementAt(rand.Next(other.Count));
            var toPlr = TShock.Players[rndPlr];  // 获得物品的玩家

            if (toPlr != null && toPlr.Active && toPlr.RealPlayer)
            {
                // 给目标玩家物品（使用自动垃圾桶的逻辑：按最大堆叠分堆给予）
                GiveItems(toPlr, id, stack);

                // 准确播报：谁得到了什么物品
                string msg = $"\n由于[c/508DC8:{GetTeamName(team)}]全体阵亡，" +
                             $"\n您的[i/s{stack}:{id}]已奖励给[c/508DC8:{toPlr.Name}]";

                // 给源玩家的提示消息
                fromPlr.SendMessage(msg, 240, 250, 150);

                // 给目标玩家的私聊消息
                toPlr.SendMessage($"\n《激励奖励》" +
                                $"\n恭喜您从[c/508DC8:{fromPlr.Name}]获得奖励[i/s{stack}:{id}]",
                                240, 250, 150);
            }
        }
    }
    #endregion

    #region 收集格子物品
    private static void CollectSlots(TSPlayer plr, string key)
    {
        var t = plr.TPlayer;
        int plrIdx = plr.Index;

        // 收集所有槽位
        var slots = new[]
        {
            (t.inventory, 0, NetItem.InventorySlots),
            (t.armor, NetItem.ArmorIndex.Item1, NetItem.ArmorSlots),
            (t.dye, NetItem.DyeIndex.Item1, NetItem.DyeSlots),
            (t.miscEquips, NetItem.MiscEquipIndex.Item1, NetItem.MiscEquipSlots),
            (t.miscDyes, NetItem.MiscDyeIndex.Item1, NetItem.MiscDyeSlots),
            (t.bank.item, NetItem.PiggyIndex.Item1, NetItem.PiggySlots),
            (t.bank2.item, NetItem.SafeIndex.Item1, NetItem.SafeSlots),
            (t.bank3.item, NetItem.ForgeIndex.Item1, NetItem.ForgeSlots),
            (t.bank4.item, NetItem.VoidIndex.Item1, NetItem.VoidSlots)
        };

        foreach (var (items, start, cnt) in slots)
        {
            for (int i = 0; i < cnt; i++)
            {
                var item = items[i];
                if (item != null && item.type > 0 && item.stack > 0)
                {
                    TeamSlot[key].Add(new Tuple<int, int, int>(item.type, plrIdx, start + i));
                }
            }
        }

        // 处理垃圾桶
        if (t.trashItem != null && t.trashItem.type > 0 && t.trashItem.stack > 0)
        {
            TeamSlot[key].Add(new Tuple<int, int, int>(t.trashItem.type, plrIdx, NetItem.TrashIndex.Item1));
        }

        // 处理三个套装栏
        for (int loadout = 0; loadout < 3; loadout++)
        {
            var armorStart = loadout == 0 ? NetItem.Loadout1Armor.Item1 :
                            loadout == 1 ? NetItem.Loadout2Armor.Item1 :
                            NetItem.Loadout3Armor.Item1;

            var dyeStart = loadout == 0 ? NetItem.Loadout1Dye.Item1 :
                          loadout == 1 ? NetItem.Loadout2Dye.Item1 :
                          NetItem.Loadout3Dye.Item1;

            for (int i = 0; i < NetItem.LoadoutArmorSlots; i++)
            {
                var item = t.Loadouts[loadout].Armor[i];
                if (item != null && item.type > 0 && item.stack > 0)
                {
                    TeamSlot[key].Add(new Tuple<int, int, int>(item.type, plrIdx, armorStart + i));
                }
            }

            for (int i = 0; i < NetItem.LoadoutDyeSlots; i++)
            {
                var item = t.Loadouts[loadout].Dye[i];
                if (item != null && item.type > 0 && item.stack > 0)
                {
                    TeamSlot[key].Add(new Tuple<int, int, int>(item.type, plrIdx, dyeStart + i));
                }
            }
        }
    }
    #endregion

    #region 移除指定格子物品并返回移除数量
    public static int RemoveSlot(TSPlayer plr, int slotIdx)
    {
        var t = plr.TPlayer;

        // 根据槽位索引找到对应的物品
        Item? item = null;

        int stack = 0;

        if (slotIdx >= 0 && slotIdx < NetItem.InventorySlots)
        {
            item = t.inventory[slotIdx];
        }
        else if (slotIdx >= NetItem.ArmorIndex.Item1 && slotIdx < NetItem.ArmorIndex.Item1 + NetItem.ArmorSlots)
        {
            item = t.armor[slotIdx - NetItem.ArmorIndex.Item1];
        }
        else if (slotIdx >= NetItem.DyeIndex.Item1 && slotIdx < NetItem.DyeIndex.Item1 + NetItem.DyeSlots)
        {
            item = t.dye[slotIdx - NetItem.DyeIndex.Item1];
        }
        else if (slotIdx >= NetItem.MiscEquipIndex.Item1 && slotIdx < NetItem.MiscEquipIndex.Item1 + NetItem.MiscEquipSlots)
        {
            item = t.miscEquips[slotIdx - NetItem.MiscEquipIndex.Item1];
        }
        else if (slotIdx >= NetItem.MiscDyeIndex.Item1 && slotIdx < NetItem.MiscDyeIndex.Item1 + NetItem.MiscDyeSlots)
        {
            item = t.miscDyes[slotIdx - NetItem.MiscDyeIndex.Item1];
        }
        else if (slotIdx >= NetItem.PiggyIndex.Item1 && slotIdx < NetItem.PiggyIndex.Item1 + NetItem.PiggySlots)
        {
            item = t.bank.item[slotIdx - NetItem.PiggyIndex.Item1];
        }
        else if (slotIdx >= NetItem.SafeIndex.Item1 && slotIdx < NetItem.SafeIndex.Item1 + NetItem.SafeSlots)
        {
            item = t.bank2.item[slotIdx - NetItem.SafeIndex.Item1];
        }
        else if (slotIdx >= NetItem.ForgeIndex.Item1 && slotIdx < NetItem.ForgeIndex.Item1 + NetItem.ForgeSlots)
        {
            item = t.bank3.item[slotIdx - NetItem.ForgeIndex.Item1];
        }
        else if (slotIdx >= NetItem.VoidIndex.Item1 && slotIdx < NetItem.VoidIndex.Item1 + NetItem.VoidSlots)
        {
            item = t.bank4.item[slotIdx - NetItem.VoidIndex.Item1];
        }
        else if (slotIdx == NetItem.TrashIndex.Item1)
        {
            item = t.trashItem;
        }
        else if (slotIdx >= NetItem.Loadout1Armor.Item1 && slotIdx < NetItem.Loadout1Armor.Item1 + NetItem.LoadoutArmorSlots)
        {
            item = t.Loadouts[0].Armor[slotIdx - NetItem.Loadout1Armor.Item1];
        }
        else if (slotIdx >= NetItem.Loadout1Dye.Item1 && slotIdx < NetItem.Loadout1Dye.Item1 + NetItem.LoadoutDyeSlots)
        {
            item = t.Loadouts[0].Dye[slotIdx - NetItem.Loadout1Dye.Item1];
        }
        else if (slotIdx >= NetItem.Loadout2Armor.Item1 && slotIdx < NetItem.Loadout2Armor.Item1 + NetItem.LoadoutArmorSlots)
        {
            item = t.Loadouts[1].Armor[slotIdx - NetItem.Loadout2Armor.Item1];
        }
        else if (slotIdx >= NetItem.Loadout2Dye.Item1 && slotIdx < NetItem.Loadout2Dye.Item1 + NetItem.LoadoutDyeSlots)
        {
            item = t.Loadouts[1].Dye[slotIdx - NetItem.Loadout2Dye.Item1];
        }
        else if (slotIdx >= NetItem.Loadout3Armor.Item1 && slotIdx < NetItem.Loadout3Armor.Item1 + NetItem.LoadoutArmorSlots)
        {
            item = t.Loadouts[2].Armor[slotIdx - NetItem.Loadout3Armor.Item1];
        }
        else if (slotIdx >= NetItem.Loadout3Dye.Item1 && slotIdx < NetItem.Loadout3Dye.Item1 + NetItem.LoadoutDyeSlots)
        {
            item = t.Loadouts[2].Dye[slotIdx - NetItem.Loadout3Dye.Item1];
        }

        if (item != null && item.type > 0)
        {
            stack = item.stack;  // 记录移除的数量

            // 直接清空整个槽位
            item.TurnToAir();

            // 发送更新数据包
            plr.SendData(PacketTypes.PlayerSlot, "", plr.Index, slotIdx);
        }

        // 返回移除的数量
        return stack;
    }
    #endregion

    #region 给予物品方法（按最大堆叠分堆给予）
    private static void GiveItems(TSPlayer plr, int id, int stack)
    {
        if (stack <= 0) return;

        // 获取物品的最大堆叠数
        var item = new Item();
        item.SetDefaults(id);
        int maxStack = item.maxStack;

        // 分堆给予
        int fullStacks = stack / maxStack;
        int remainder = stack % maxStack;

        for (int i = 0; i < fullStacks; i++)
            plr.GiveItem(id, maxStack);

        if (remainder > 0)
            plr.GiveItem(id, remainder);
    }
    #endregion

    #region 清理激励数据
    public static void ClearReward(TSPlayer plr, int teamId)
    {
        // 检查该队伍是否还有其他在线玩家
        bool has = false;
        for (int i = 0; i < TShock.Players.Length; i++)
        {
            var p = TShock.Players[i];
            if (p != null && p.IsLoggedIn && p.Team == teamId && p.Index != plr.Index)
            {
                has = true;
                break;
            }
        }

        // 如果没有其他在线玩家，清理该队伍的激励数据
        if (!has)
        {
            RemoveReward(teamId);
        }
    }

    public static void RemoveReward(int teamId)
    {
        string teamKey = teamId.ToString();
        if (TeamSlot.ContainsKey(teamKey))
            TeamSlot[teamKey].Clear();

        if (TeamRew.ContainsKey(teamId))
            TeamRew.Remove(teamId);
    }
    #endregion
}