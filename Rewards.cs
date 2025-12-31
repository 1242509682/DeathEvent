using TShockAPI;
using Terraria;
using static DeathEvent.Data;
using static DeathEvent.DeathEvent;

namespace DeathEvent;

internal class Rewards
{
    #region 激励功能
    public static void DoReward(TSPlayer plr, HashSet<int> other, List<TSPlayer> teamPly)
    {
        if (!Config.Team || !Config.Incentive || other.Count == 0) return;

        int team = plr.Team;
        var data = GetData(team);

        if (data.DoReward) return;

        data.DoReward = true;
        data.SlotList.Clear();

        if (teamPly.Count == 0) return;

        // 筛选有效其他队伍玩家
        var validOth = other.Where(idx =>
        {
            var p = TShock.Players[idx];
            return p != null && p.RealPlayer && p.Active;
        }).ToList();

        if (validOth.Count == 0) return;

        var rand = Main.rand;

        foreach (var p in teamPly)
        {
            // 跳过白名单玩家
            if (Config.WhiteList.Contains(p.Name)) continue;

            // 收集玩家物品槽位
            var plySlots = new List<ItemSlotInfo>();
            CollectSlots(p, plySlots);

            if (plySlots.Count == 0) continue;

            // 随机抽取一个物品
            int rndSlot = rand.Next(plySlots.Count);
            var selSlot = plySlots[rndSlot];

            // 移除物品
            int stack = RemoveSlot(p, selSlot.SlotIndex);
            if (stack <= 0) continue;

            // 选择目标玩家
            int rndPlr = validOth[rand.Next(validOth.Count)];
            var toPlr = TShock.Players[rndPlr];

            if (toPlr == null || !toPlr.RealPlayer || !toPlr.Active) continue;

            // 给予物品
            GiveItems(toPlr, selSlot.ItemId, stack);

            // 发送个人消息
            SendRewMsg(p, toPlr, selSlot.ItemId, stack);
        }
    }
    #endregion

    #region 发送消息
    private static void SendRewMsg(TSPlayer from, TSPlayer to, int id, int stack)
    {
        string itemIcon = Tool.ItemIconStack(id, stack);

        // 给源玩家
        from.SendMessage($"您的{itemIcon}已奖励给[c/508DC8:{to.Name}]", 240, 250, 150);

        // 给目标玩家
        to.SendMessage($"恭喜您从[c/508DC8:{from.Name}]获得奖励{itemIcon}", 240, 250, 150);
    }
    #endregion

    #region 给予物品
    private static void GiveItems(TSPlayer plr, int id, int stack)
    {
        if (stack <= 0) return;

        var item = new Item();
        item.SetDefaults(id);
        int maxStack = item.maxStack;

        int fullStacks = stack / maxStack;
        int rem = stack % maxStack;

        for (int i = 0; i < fullStacks; i++)
            plr.GiveItem(id, maxStack);

        if (rem > 0)
            plr.GiveItem(id, rem);
    }
    #endregion

    #region 收集槽位
    private static void CollectSlots(TSPlayer plr, List<ItemSlotInfo> slotList)
    {
        var t = plr.TPlayer;
        int idx = plr.Index;

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
                    AddSlot(slotList, plr, start + i, item.type, item.stack);
                }
            }
        }

        // 垃圾桶
        if (t.trashItem != null && t.trashItem.type > 0 && t.trashItem.stack > 0)
        {
            AddSlot(slotList, plr, NetItem.TrashIndex.Item1, t.trashItem.type, t.trashItem.stack);
        }

        // 套装栏
        for (int lod = 0; lod < 3; lod++)
        {
            var armorStart = lod == 0 ? NetItem.Loadout1Armor.Item1 :
                            lod == 1 ? NetItem.Loadout2Armor.Item1 :
                            NetItem.Loadout3Armor.Item1;

            var dyeStart = lod == 0 ? NetItem.Loadout1Dye.Item1 :
                          lod == 1 ? NetItem.Loadout2Dye.Item1 :
                          NetItem.Loadout3Dye.Item1;

            for (int i = 0; i < NetItem.LoadoutArmorSlots; i++)
            {
                var item = t.Loadouts[lod].Armor[i];
                if (item != null && item.type > 0 && item.stack > 0)
                {
                    AddSlot(slotList, plr, armorStart + i, item.type, item.stack);
                }
            }

            for (int i = 0; i < NetItem.LoadoutDyeSlots; i++)
            {
                var item = t.Loadouts[lod].Dye[i];
                if (item != null && item.type > 0 && item.stack > 0)
                {
                    AddSlot(slotList, plr, dyeStart + i, item.type, item.stack);
                }
            }
        }
    }
    #endregion

    #region 添加槽位方法（统一封装）
    private static void AddSlot(List<ItemSlotInfo> list, TSPlayer plr, int slotIdx, int itemId, int stack)
    {
        list.Add(new ItemSlotInfo
        {
            PlayerIndex = plr.Index,
            PlayerName = plr.Name,
            SlotIndex = slotIdx,
            ItemId = itemId,
            ItemStack = stack
        });
    }
    #endregion

    #region 移除物品
    public static int RemoveSlot(TSPlayer plr, int slotIdx)
    {
        var t = plr.TPlayer;
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
            stack = item.stack;
            item.TurnToAir();
            plr.SendData(PacketTypes.PlayerSlot, "", plr.Index, slotIdx);
        }

        return stack;
    }
    #endregion
}