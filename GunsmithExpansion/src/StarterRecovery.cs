using System;
using System.Collections.Generic;

namespace GunsmithExpansion
{
    // One-time starter item recovery for old saves (final-strategy 362; current/T-041 B-03):
    //   - Default-off boolean switch in ModConfig ("Save recovery").
    //   - When turned on by the player, the next ModHook.OnGameLoadedLate scans EmporiumEntry.Instance.GetAllOwnedItems(false, true),
    //     grants any missing starter items (repair certificate matching highest recorded repair rank >= 1, gunsmith neon sign,
    //     Gunsmithing Manual, Firearms Catalog, and a 2-slot starter Pistol Cabinet if no gun cabinet is owned), notifies the player,
    //     and immediately turns the switch back off and saves the config.
    internal static class StarterRecovery
    {
        private const string SettingKey = "recover_missing_starter_items";
        // Latin letters only, as in Cabinets.SettingsSection (ModConfigWindow section headers do not apply ScriptFontSwap).
        private const string SettingsSection = "Save recovery";
        private static ConfigEntry<bool> recoverEntry;

        internal static void DefineSettings()
        {
            ModConfig config = ModHelper.GetConfig(GunsmithMod.ModId);
            if (config == null)
                return;
            recoverEntry = config.Define(SettingKey, false, SettingsSection,
                () => Text.Pick("进档时补发缺失的枪匠初始物品（一次性）", "Grant missing gunsmith starter items on load (one-time)"),
                () => Text.Pick(
                    "打开后，下次进入存档时会检查已拥有的物品，自动补发缺失的维修证书（按当前最高维修等级，至少 1 级）、枪匠霓虹灯牌、《枪匠手册》、《枪械目录》与初始手枪柜（未拥有任何枪柜时发放 1 个解锁 2 格的手枪柜），随后该选项会自动关闭。默认关闭。",
                    "When on, the next save you load is checked for owned items and any missing starter items are granted: your repair certificate (at your highest recorded repair rank, at least level 1), the Gunsmith Neon Sign, the Gunsmithing Manual, the Firearms Catalog, and a starter Pistol Cabinet with 2 unlocked slots (only if you own no gun cabinet). This switch then turns itself off automatically. Off by default."));
            config.Save();
        }

        internal static void OnGameLoaded()
        {
            if (recoverEntry == null || !recoverEntry.Value)
                return;
            if (!PlayerStore.HasInstance || EmporiumEntry.Instance == null)
                return;

            var granted = new List<string>();
            try
            {
                EmporiumEntry entry = EmporiumEntry.Instance;
                GraphNodeStorage place = entry.invElement;
                bool hasCert = false;
                bool hasSign = false;
                bool hasManual = false;
                bool hasCatalog = false;
                bool hasCabinet = false;

                foreach (GameItem item in entry.GetAllOwnedItems(false, true))
                {
                    if (item == null || string.IsNullOrEmpty(item.identifier))
                        continue;
                    if (Certificates.LevelOf(item) > 0)
                        hasCert = true;
                    if (item.IsID(GunsmithSign.Id))
                        hasSign = true;
                    if (item.IsID(Manual.GunsmithingManual.ItemId))
                        hasManual = true;
                    if (item.IsID(Manual.FirearmsCatalog.ItemId))
                        hasCatalog = true;
                    if (Cabinets.IsCabinet(item) || item.IsID(Cabinets.SmallId) || item.IsID(Cabinets.LargeId))
                        hasCabinet = true;
                }

                if (!hasCert)
                {
                    int targetRank = Math.Max(1, Certificates.Rank());
                    if (DirectoryMaster.Has<GameItem>(Certificates.ItemId(targetRank)))
                    {
                        Certificates.GrantFirst(place);
                        granted.Add(Certificates.Name(targetRank));
                        GunsmithMod.Log("starter recovery: repair certificate level " + targetRank + " given");
                    }
                }

                if (!hasSign && GunsmithSign.Registered())
                {
                    GameItem sign = GunsmithSign.NewSign(true);
                    if (sign != null)
                    {
                        GraphUtils.EmporiumTryAdd(place, sign);
                        SaveRecord.Given("gunsmith_sign");
                        granted.Add(sign.name);
                        GunsmithMod.Log("starter recovery: gunsmith sign given");
                    }
                }

                if (!hasManual && Manual.GunsmithingManual.Registered)
                {
                    GameItem book = DirectoryMaster.Item(Manual.GunsmithingManual.ItemId, true);
                    if (book != null)
                    {
                        GraphUtils.EmporiumTryAdd(place, book);
                        granted.Add(Text.Pick(Manual.GunsmithingManual.NameZh, Manual.GunsmithingManual.NameEn));
                        GunsmithMod.Log("starter recovery: " + Manual.GunsmithingManual.ItemId + " given");
                    }
                }

                if (!hasCatalog && Manual.FirearmsCatalog.Registered)
                {
                    GameItem book = DirectoryMaster.Item(Manual.FirearmsCatalog.ItemId, true);
                    if (book != null)
                    {
                        GraphUtils.EmporiumTryAdd(place, book);
                        granted.Add(Text.Pick(Manual.FirearmsCatalog.NameZh, Manual.FirearmsCatalog.NameEn));
                        GunsmithMod.Log("starter recovery: " + Manual.FirearmsCatalog.ItemId + " given");
                    }
                }

                if (!hasCabinet && Cabinets.Registered(Cabinets.SmallId))
                {
                    GameItem box = Cabinets.GrantStarter(place, "starter recovery");
                    if (box != null)
                        granted.Add(Cabinets.Name(true));
                }

                string message = granted.Count > 0
                    ? string.Format(Text.Pick(
                        "已补发缺失的枪匠初始物品：{0}。该恢复选项已自动关闭。",
                        "Granted missing gunsmith starter items: {0}. The recovery switch has been turned off automatically."),
                        string.Join(Text.Pick("、", ", "), granted.ToArray()))
                    : Text.Pick(
                        "当前存档已拥有全部枪匠初始物品，无需补发。该恢复选项已自动关闭。",
                        "This save already owns all gunsmith starter items; nothing needed to be granted. The recovery switch has been turned off automatically.");

                GunsmithMod.Log("starter recovery complete: granted " + granted.Count + " item(s)");
                if (StoreUIManager.Instance != null)
                    StoreUIManager.Instance.Notify(message, "default");
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("starter recovery: " + e);
            }
            finally
            {
                try
                {
                    recoverEntry.Set(false);
                    ModConfig config = ModHelper.GetConfig(GunsmithMod.ModId);
                    if (config != null)
                        config.Save();
                }
                catch (Exception e)
                {
                    GunsmithMod.Warn("starter recovery reset switch: " + e);
                }
            }
        }
    }
}
