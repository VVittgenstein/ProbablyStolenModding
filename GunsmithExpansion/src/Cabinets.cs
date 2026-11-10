using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace GunsmithExpansion
{
    // Gun cabinets (final-strategy 85, 87, 99; VVittgenstein 2026-11-03 19:25 from the players, 20:35, 21:18 point 1, 21:49
    // points 3 and 5, 21:59 point 4, 22:44 point 5; pictures current/006 unit U129). Made like the game's toolbox
    // (ToolDirectory.CreateToolbox, ToolboxHelper, read 2026-11-03): a machinery window of slots, one item a slot, the
    // slots unlocked one at a time with scrap metal.
    //   - The picture goes by the slots unlocked and the guns inside (item 71; VVittgenstein 2026-11-05 23:38 and 2026-11-06 05:09, current/008 quotes 18 and 27):
    //     gse_cabinet_*_<slots>_<guns>, 28 pictures a cabinet (tools/compose_cabinets.py), changed as the game's beer case
    //     changes its picture (ContainerItemDirectory.UpdateBeerCaseSprite). The guns' shadows fill the first slots whichever slots they are in (Agent's decision). The _old and _new
    //     pictures of 0.6.x stay in Textures/Items: a save restores the picture it was saved with before Refresh runs (SaveManager.DecodeNodesInternal).
    //   - Two cabinets, by the names VVittgenstein gave them (current/008 quote 22 point 71): the pistol cabinet (手枪柜, "small" here), 2 x 3 cells,
    //     pistols only; the long gun cabinet (大枪柜, "large" here), 3 x 4 cells, only guns that are not pistols. Until 0.7 it took any gun; a
    //     pistol a save has in one stays there until it is taken out (SaveManager.DecodeNodesInternal does not ask the slot). 6 slots each,
    //     one gun a slot; only the store's own guns go in (as the toolbox takes only owned tools).
    //   - A cabinet bought or ordered has 2 slots unlocked; scrap metal dragged onto it unlocks one more (used up as
    //     the toolbox's, StartingPerkContent.ConsumeUpgradeMaterial). A metal ingot dragged onto it unlocks them all
    //     (VVittgenstein 2026-11-05 13:59, current/008 quote 4 point 28). Why: an ingot is worth 50 and scrap metal 10, and filling a
    //     cabinet takes 4 scrap metal from 2 slots (40) or 6 from none (60), so one ingot costs about what the scrap
    //     costs and saves the dragging; an ingot for one slot would cost five times the scrap and nobody would use it.
    //   - The value goes by the slots unlocked (VVittgenstein 2026-11-04 21:24 and 21:37, current/006 quotes 56 and 57): small 50 and
    //     25 more a slot, 200 full; large 100 and 35 more a slot, 310 full; at 2 slots, as bought or ordered, that is 100 and
    //     170 (Agent's reading of the rule for the slots a bought one starts with). Until this change: 150 and 300 at any
    //     slots, plus the scrap's value at each upgrade.
    //   - In the display case the guns inside count as the display case's own items: for the types and value that
    //     draw customers (ShowcaseHelper.GetTypeTotalValue, GetTypeCount, PlayerStore.GetTypesByValue) and for the
    //     game's arrests and stolen goods checks (IsShowCaseContainSeriousContraband, IsShowCaseContainSeriousStolen).
    //   - Found at the dumping grounds with no slot unlocked, in 0.5% of the scavenges by default, half of them each
    //     kind, the percentage a setting of the mod (VVittgenstein 2026-11-05 13:59, current/008 quote 4 point 27; DefineSettings):
    //     the game's table has its tools at 2% in all, about 0.33% each (value 25 to 100), and its tier two modules at 0.5%
    //     (value 70 to 100), and a cabinet found is worth 50 or 100. From 0.6.0 to 0.6.2 it was 2%.
    //   - Ordered by phone from the retired gunsmith (Phone). He also brings a long gun cabinet on his first visit from day LargeVisitDay
    //     on, which is day 18 by his visits on days 4, 11, 18, 25 (StoreClientManager.HandleContentUnlockClient); VVittgenstein asked for a day
    //     (current/008 quote 22 point 71), day 14 is the Agent's: from then on the third batch fills up with random customers and private
    //     security contractors come for guns on display.
    // Agent's decisions: which guns are pistols (this mod's pistols and its revolvers of at most 6 cells' width, which leaves out the
    // revolver carbine, the game's pistols, revolver and stun gun; until 0.7 the width was at most 5), the slot sizes.
    internal static class Cabinets
    {
        internal const string SmallId = "gse_cabinet_small";
        internal const string LargeId = "gse_cabinet_large";
        internal const string Tag = "GSE_CABINET";
        internal const string SlotsTag = "GSE_CABINET_SLOTS_INT";
        internal const int SlotCount = 6;
        internal const int StartSlots = 2;
        internal const float DefaultDumpPercent = 0.5f;
        private const string DumpPercentKey = "cabinet_dump_percent";
        // Latin letters only: the game's settings window shows the section title in its default font without the font swap it gives the mod list
        // (ModConfigWindow.Open, CustomUIBuilder.AddHeader; ScriptFontSwap.Apply only in ModElement.Init and ModUIManager.ShowMod), so Chinese and
        // Cyrillic may not show in western languages (read from the IL, not seen in the game). The settings' labels and tooltips keep their languages
        // (VVittgenstein 2026-11-07 20:16, current/T-006 quote 21; until then "枪柜 / Gun cabinets / Оружейные шкафы").
        private const string SettingsSection = "Gun cabinets";
        private const string ScrapId = "scrap_metal";
        private const string IngotId = "metal_ingot";
        internal const int SmallEmptyValue = 50;
        internal const int SmallSlotValue = 25;
        internal const int LargeEmptyValue = 100;
        internal const int LargeSlotValue = 35;
        internal const int LargeVisitDay = 14;
        private const int MaxRevolverWidth = 6;
        private const string LargeBroughtKey = "cabinet_large_brought";
        private const string StunGunId = "stun_gun";
        private static readonly System.Random rng = new System.Random();
        private static ConfigEntry<float> dumpPercent;
        private static readonly HashSet<string> VanillaPistols = new HashSet<string>
        {
            "glock_receiver", "printed_gun", "handmade_pistol", "heavy_handmade_pistol", "revolver", StunGunId,
        };

        private sealed class State
        {
            internal bool Small;
            internal List<GameSlotInventory> Slots;
            internal List<MachineryLabel> Labels;
            internal PixelWindow Window;
        }

        private static readonly ConditionalWeakTable<GameItem, State> states = new ConditionalWeakTable<GameItem, State>();

        internal static bool Registered(string id)
        {
            return ModHelper.ModFileExists(GunsmithMod.ModId, "Textures/Items/" + Picture(id, StartSlots, 0) + ".png");
        }

        private static string Picture(string id, int unlocked, int guns)
        {
            return id + "_" + unlocked + "_" + guns;
        }

        // The dumping grounds' percentage: an entry of the game's mod configuration (ModHelper.GetConfig), shown in this
        // mod's settings window in the Mods panel and kept in <persistentDataPath>\ModConfigs\GunsmithExpansion.json under
        // cabinet_dump_percent, which can also be edited while the game is closed. No range is given, so the game's window
        // shows a text box (ModConfigWindow.BuildEntry) and 0.25 can be typed exactly; a slider would not hit it.
        internal static void DefineSettings()
        {
            ModConfig config = ModHelper.GetConfig(GunsmithMod.ModId);
            dumpPercent = config.Define(DumpPercentKey, DefaultDumpPercent, SettingsSection,
                () => Text.Pick("垃圾场捡到枪柜的概率（%）", "Chance of finding a gun cabinet when scavenging (%)"), null, null,
                () => Text.Pick("每次在垃圾场拾荒捡到枪柜的概率，单位是百分比，手枪柜和大枪柜各占一半。默认 0.5，填 0 就不会捡到。",
                    "The chance per scavenge at the dumping grounds, in percent, half pistol cabinets and half long gun cabinets. 0.5 by default; 0 finds none."));
            config.Save();
        }

        internal static float DumpPercent()
        {
            float value = dumpPercent != null ? dumpPercent.Value : DefaultDumpPercent;
            return float.IsNaN(value) ? DefaultDumpPercent : Math.Max(0f, Math.Min(100f, value));
        }

        internal static void Register(ModItemDirectory directory)
        {
            int n = 0;
            foreach (string id in new[] { SmallId, LargeId })
            {
                string cabinet = id;
                if (!Registered(cabinet))
                    continue;
                directory.Add(cabinet, () => Create(directory, cabinet == SmallId));
                n++;
            }
            GunsmithMod.Log("registered " + n + " gun cabinets");
        }

        internal static bool IsCabinet(GameItem item)
        {
            return item != null && item.IsTag(Tag);
        }

        // While the directory builds a cabinet its identifier is not set yet (GameItem.IsID would throw), so the kind is
        // kept with the cabinet's slots.
        private static bool IsSmall(GameItem box)
        {
            State state;
            if (box != null && states.TryGetValue(box, out state))
                return state.Small;
            return box != null && box.identifier == SmallId;
        }

        internal static string Name(bool small)
        {
            return small ? Text.Pick("手枪柜", "Pistol Cabinet") : Text.Pick("大枪柜", "Long Gun Cabinet");
        }

        internal static int Unlocked(GameItem box)
        {
            return box != null && box.IsTag(SlotsTag) ? Math.Max(0, Math.Min(SlotCount, box.GetTagReadonly(SlotsTag).GetInt())) : StartSlots;
        }

        internal static void SetUnlocked(GameItem box, int slots)
        {
            int value = Math.Max(0, Math.Min(SlotCount, slots));
            box.EnableTag(SlotsTag, false);
            box.ModifyTag(SlotsTag, s => s.SetInt(value), false);
            Refresh(box);
        }

        // Guns carry ADV_GUN_TAG (GunHelper.InitGun). The game's stun gun is the one gun of GunsItemDirectory made without
        // InitGun (type WEAPON, no FIREARM), so it is named here (VVittgenstein 2026-11-04 21:45,
        // current/006 quote 59). The taser is the game's melee weapon and the pistol frames are parts: not guns.
        internal static bool IsGun(GameItem item)
        {
            return item != null && (item.IsTag("ADV_GUN_TAG") || item.identifier == StunGunId);
        }

        internal static bool IsPistol(GameItem item)
        {
            if (!IsGun(item))
                return false;
            if (VanillaPistols.Contains(item.identifier))
                return true;
            GunDef d = RepairOrders.Find(item.identifier);
            if (d == null)
                return false;
            // every pistol of the catalog is Slots.Pistol; the 2077 Add-On's has no gun mod slots
            if (d.Slots == Slots.Pistol || d.ClassName == "手枪")
                return true;
            int width;
            string size = d.Size ?? "";
            int x = size.IndexOf('x');
            return d.ClassName == "左轮（含左轮卡宾）" && x > 0 && int.TryParse(size.Substring(0, x), out width) && width <= MaxRevolverWidth;
        }

        private static bool Accepts(GameItem box, int slot, GameItem item)
        {
            return slot < Unlocked(box) && GeneralHelper.IsItemOwned(item) && (IsSmall(box) ? IsPistol(item) : IsGun(item) && !IsPistol(item));
        }

        private static GameItem Create(ModItemDirectory directory, bool small)
        {
            var slots = new List<GameSlotInventory>();
            var labels = new List<MachineryLabel>();
            var panels = new List<MachineryPanel>();
            for (int i = 0; i < SlotCount; i++)
            {
                var slot = new GameSlotInventory(small ? 3 : 5, 2, false, false);
                MachineryPanel panel = MachineHelper.Panel(SlotLabel(i), slot, "slot" + i, true);
                slots.Add(slot);
                labels.Add(panel.label);
                panels.Add(panel);
            }
            PixelWindow window = MachineHelper.CreateMachineryWindow(true, panels.ToArray());
            MachineryPanelRow row = window.childElement as MachineryPanelRow;
            if (row != null)
                row.SetColumns(small ? 3 : 2);
            window.SetTitle(Name(small));
            window.Validate();

            string id = small ? SmallId : LargeId;
            GameItem box = directory.Empty().SetContentWindow(window).SetSpriteAndShapeFromMod(GunsmithMod.ModId, Picture(id, StartSlots, 0));
            box.name = Name(small);
            box.flavorText = Text.Pick("带玻璃门的金属枪柜。", "A metal gun cabinet with a glass door.");
            box.EnableTag("IMPORTANT_TAG", false);
            box.EnableTag("CONTAINER_TAG", false);
            box.EnableTag(Tag, false);
            box.SetGameItemType("STORAGE");
            ContainerHelper.AddContainerTooltipTag(box);
            box.EnableTag(SlotsTag, false);
            box.ModifyTag(SlotsTag, s => s.SetInt(StartSlots), false);
            for (int i = 0; i < SlotCount; i++)
            {
                int index = i;
                GameSlotInventory slot = slots[i];
                slot.identifier = "gse_cabinet_slot_" + i;
                slot.mayInventoryAddItemFunc = (Func<GameItem, GameInventory, bool>)Delegate.Combine(slot.mayInventoryAddItemFunc,
                    new Func<GameItem, GameInventory, bool>((item, inv) => Accepts(box, index, item)));
                slot.onSlotAddItemEndFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Combine(slot.onSlotAddItemEndFunc,
                    new Action<GameItem, GameInventory, SlotMarker>((item, inv, marker) => UpdatePicture(box)));
                slot.onSlotAddItemEndFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Combine(slot.onSlotAddItemEndFunc,
                    new Action<GameItem, GameInventory, SlotMarker>((item, inv, marker) => OnPutInto(box, item)));
                slot.onSlotRemoveItemEndFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Combine(slot.onSlotRemoveItemEndFunc,
                    new Action<GameItem, GameInventory, SlotMarker>((item, inv, marker) => UpdatePicture(box)));
            }
            states.Remove(box);
            states.Add(box, new State { Small = small, Slots = slots, Labels = labels, Window = window });
            window.openCallback = (Action)Delegate.Combine(window.openCallback, new Action(() => Refresh(box)));
            box.onLoaded = (Action<GameItem>)Delegate.Combine(box.onLoaded, new Action<GameItem>(b => Refresh(b)));
            AudioHelper.InitSmallMachine(box);
            Refresh(box);
            return box;
        }

        private static string SlotLabel(int i)
        {
            return LocHelper.GetLocalizedMechanic("mech_toolbox_label_slot", i + 1);
        }

        internal static int ValueOf(bool small, int unlocked)
        {
            return small ? SmallEmptyValue + SmallSlotValue * unlocked : LargeEmptyValue + LargeSlotValue * unlocked;
        }

        // Value, labels, picture and description by the slots unlocked (the last three as ToolboxHelper.RefreshSlots and
        // UpdateToolboxSprite). A cabinet of an older save takes its value here when it is loaded.
        internal static void Refresh(GameItem box)
        {
            if (box == null)
                return;
            int unlocked = Unlocked(box);
            bool small = IsSmall(box);
            box.unitValue = ValueOf(small, unlocked);
            // a saved item keeps the name it was saved with (SaveManager.DecodeNodesInternal)
            box.name = Name(small);
            State state;
            if (states.TryGetValue(box, out state))
            {
                for (int i = 0; i < state.Labels.Count; i++)
                {
                    bool open = i < unlocked;
                    state.Labels[i].SetText(open ? SlotLabel(i) : LocHelper.GetLocalizedMechanic("mech_toolbox_label_locked"));
                    state.Labels[i].SetTextColor(open ? MachineryStyle.LABEL_TEXT : MachineryStyle.BUTTON_TEXT_DISABLED);
                }
                if (state.Window != null)
                    state.Window.Validate();
            }
            UpdatePicture(box);
            box.shortDescription = (small ? Text.Pick("只能存放手枪，每个栏位一把。", "Holds pistols only, one per slot.")
                    : Text.Pick("只能存放手枪以外的枪，每个栏位一把。", "Holds guns other than pistols only, one per slot."))
                + Text.Pick("放进展示柜时，柜内的枪按展示柜里的物品计算。", " In the display case, the guns inside count as display case items.")
                + " " + LocHelper.GetLocalizedMechanic("mech_toolbox_tooltip_slots", unlocked, SlotCount)
                + (unlocked < SlotCount ? " " + LocHelper.GetLocalizedMechanic("mech_toolbox_tooltip_upgrade")
                    + Text.Pick("金属锭可以一次解锁全部栏位。", " A metal ingot unlocks every slot at once.") : "");
        }

        private static void UpdatePicture(GameItem box)
        {
            int unlocked = Unlocked(box);
            int guns = 0;
            foreach (GameSlotInventory slot in SlotsOf(box))
                guns += slot.childItems.Count;
            box.SetSpriteAndShapeFromMod(GunsmithMod.ModId, Picture(IsSmall(box) ? SmallId : LargeId, unlocked, Math.Min(guns, unlocked)));
        }

        // The guns are back in the slots only when the whole save is read, so the pictures are set again then (ModHook.OnGameLoadedLate).
        internal static void OnGameLoaded()
        {
            try
            {
                if (EmporiumEntry.Instance == null)
                    return;
                foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
                {
                    if (IsCabinet(item))
                        UpdatePicture(item);
                }
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("gun cabinet pictures on load: " + e.Message);
            }
        }

        internal static List<GameSlotInventory> SlotsOf(GameItem box)
        {
            State state;
            return box != null && states.TryGetValue(box, out state) ? state.Slots : new List<GameSlotInventory>();
        }

        internal static bool MayUpgrade(GameItem source, GameItem box)
        {
            return source != null && (source.IsID(ScrapId) || source.IsID(IngotId)) && IsCabinet(box)
                && GeneralHelper.IsItemOwned(source) && GeneralHelper.IsItemOwned(box) && Unlocked(box) < SlotCount;
        }

        internal static void Upgrade(GameItem source, GameItem box)
        {
            SetUnlocked(box, source.IsID(IngotId) ? SlotCount : Unlocked(box) + 1);
            StartingPerkContent.ConsumeUpgradeMaterial(source);
            if (AudioManager.Instance != null)
                AudioManager.Instance.Play2("scrap_metal_drop");
            GunsmithMod.Log(box.identifier + " upgraded to " + Unlocked(box) + " slots");
        }

        internal static string UseLine(GameItem source, GameItem box)
        {
            return source.IsID(IngotId)
                ? string.Format(Text.Pick("<b>升级</b><u>{0}</u>，解锁全部栏位", "<b>Upgrade</b> <u>{0}</u>, unlocking every slot"), box.GetDisplayName(true))
                : string.Format(Text.Pick("<b>升级</b><u>{0}</u>，解锁一个栏位", "<b>Upgrade</b> <u>{0}</u>, unlocking a slot"), box.GetDisplayName(true));
        }

        // Item 31 (VVittgenstein 2026-11-07 20:06, current/T-006 quote 20): a cabinet put into the display case, or a gun put into a cabinet there, while a security officer is the
        // customer. The game has him react to an item put straight into the case (PlayerStore.AddedItemToDisplayCase calls GameItem.OnAddedToShowcaseArea, which hands
        // the item to the action StoreClient.CompleteSecClient sets): to an owned contraband item, else to an owned stolen item he finds (StolenHelper.GetFoundChance).
        // The guns in the cabinet are handed to that action the same way: the first contraband gun, else each stolen gun until he reacts (he is then no longer
        // dismissable). With no customer, as on loading, nothing happens.
        internal static void OnPutOnShow(List<GameItem> guns)
        {
            PlayerStore store = PlayerStore.Instance;
            StoreClient client = store != null && store.currentClientInstance != null ? store.currentClientInstance.GetClientBlueprint() : null;
            if (client == null || client.OnItemAddedToShowcase == null || guns == null)
                return;
            GameItem contraband = guns.Find(g => g != null && GeneralHelper.IsItemOwned(g) && g.IsTag("CONTRABAND_ITEM_TAG"));
            if (contraband != null)
            {
                client.OnItemAddedToShowcase(contraband);
                return;
            }
            foreach (GameItem gun in guns)
            {
                if (gun == null || !gun.IsTag("STOLEN_TAG"))
                    continue;
                client.OnItemAddedToShowcase(gun);
                if (!client.dismissable)
                    return;
            }
        }

        private static void OnPutInto(GameItem box, GameItem item)
        {
            try
            {
                if (IsGun(item) && EmporiumEntry.Instance != null && box.parentInventory != null && box.parentInventory == EmporiumEntry.Instance.showcaseElement)
                    OnPutOnShow(new List<GameItem> { item });
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("gun put into a cabinet on show: " + e.Message);
            }
        }

        // The guns in the cabinets of the display case.
        internal static List<GameItem> DisplayedGuns()
        {
            var guns = new List<GameItem>();
            if (EmporiumEntry.Instance == null || EmporiumEntry.Instance.showcaseElement == null)
                return guns;
            foreach (GameItem item in EmporiumEntry.Instance.showcaseElement.childItems)
            {
                if (!IsCabinet(item))
                    continue;
                guns.AddRange(GraphUtils.FindAllChildrenType<GameItem>(item, IsGun, g => !IsGun(g)));
            }
            return guns;
        }

        // The guns of the display case: those in its cabinets and those lying in it.
        internal static List<GameItem> ShowcaseGuns()
        {
            List<GameItem> guns = DisplayedGuns();
            if (EmporiumEntry.Instance == null || EmporiumEntry.Instance.showcaseElement == null)
                return guns;
            foreach (GameItem item in EmporiumEntry.Instance.showcaseElement.childItems)
            {
                if (IsGun(item))
                    guns.Add(item);
            }
            return guns;
        }

        // A pistol cabinet for the gunsmith start and the retired gunsmith service (VVittgenstein 2026-11-04, current/006 quotes 35
        // point 4 and 36), owned, with the slots of a bought one (Agent's decision).
        internal static GameItem GrantStarter(GraphNodeStorage place, string why)
        {
            if (!Registered(SmallId) || place == null)
                return null;
            GameItem box = DirectoryMaster.Item(SmallId, true);
            if (box == null)
                return null;
            GeneralHelper.SetItemOwned(box, true);
            GraphUtils.EmporiumTryAdd(place, box);
            SaveRecord.Given("starter_cabinet");
            GunsmithMod.Log("a pistol cabinet given (" + why + ")");
            return box;
        }

        // The retired gunsmith's visit that brings a long gun cabinet, once in a game. It goes on the counter before his other goods; if the
        // counter still has no room he brings it next time.
        internal static void StockLarge(StoreClient client)
        {
            if (client == null || StoreStation.GetDayCounter() < LargeVisitDay || !Registered(LargeId) || ModHelper.GetModDataBool(GunsmithMod.ModId, LargeBroughtKey, false))
                return;
            Sellers.BeforeStock(client, () =>
            {
                if (PlayerStore.Instance == null || !Sellers.Offer(LargeId))
                    return;
                ModHelper.SetModDataBool(GunsmithMod.ModId, LargeBroughtKey, true);
                GunsmithMod.Log("the retired gunsmith brings a long gun cabinet");
            });
        }

        internal static GameItem NewDumpFind()
        {
            GameItem box = DirectoryMaster.Item(rng.Next(2) == 0 ? SmallId : LargeId, true);
            SetUnlocked(box, 0);
            return box;
        }

        internal static void AddDumpFind(List<GameItem> found)
        {
            if (found == null || rng.NextDouble() * 100.0 >= DumpPercent() || !Registered(SmallId) || !Registered(LargeId))
                return;
            found.Add(NewDumpFind());
            GunsmithMod.Log("a gun cabinet found at the dumping grounds");
        }
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetTypeTotalValue))]
    internal static class CabinetTypeValuePatch
    {
        private static void Postfix(string type, ref int __result)
        {
            foreach (GameItem gun in Cabinets.DisplayedGuns())
            {
                if (gun.GetGameItemType().Contains(type))
                    __result += (int)gun.GetCurrentValue(false, true, true, false, 0);
            }
        }
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetTypeCount))]
    internal static class CabinetTypeCountPatch
    {
        private static void Postfix(string type, ref int __result)
        {
            foreach (GameItem gun in Cabinets.DisplayedGuns())
            {
                if (gun.GetGameItemType().Contains(type))
                    __result++;
            }
        }
    }

    // ShowcaseHelper.GetDisplayCaseType ranks the display case's items through this method. Advertising mods may
    // add wall-mounted signs from another inventory to that list. Expand each included cabinet that is physically
    // in the display case; unrelated entries must not prevent its guns from contributing. Keep the caller's list
    // intact and do not add guns already included by another integration.
    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.GetTypesByValue))]
    internal static class CabinetTypesByValuePatch
    {
        private static void Prefix(ref List<GameItem> items)
        {
            if (items == null || items.Count == 0 || EmporiumEntry.Instance == null || EmporiumEntry.Instance.showcaseElement == null)
                return;
            GameInventory showcase = EmporiumEntry.Instance.showcaseElement;
            List<GameItem> expanded = null;
            foreach (GameItem item in items)
            {
                if (!Cabinets.IsCabinet(item) || !ReferenceEquals(item.parentInventory, showcase))
                    continue;
                foreach (GameItem gun in GraphUtils.FindAllChildrenType<GameItem>(item, Cabinets.IsGun, g => !Cabinets.IsGun(g)))
                {
                    if ((expanded ?? items).Exists(existing => ReferenceEquals(existing, gun)))
                        continue;
                    if (expanded == null)
                        expanded = new List<GameItem>(items);
                    expanded.Add(gun);
                }
            }
            if (expanded != null)
                items = expanded;
        }
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.IsShowCaseContainSeriousContraband))]
    internal static class CabinetContrabandPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (__result)
                return;
            foreach (GameItem gun in Cabinets.DisplayedGuns())
            {
                if (gun.IsTag("CONTRABAND_ITEM_TAG"))
                {
                    __result = true;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(ShowcaseHelper), nameof(ShowcaseHelper.IsShowCaseContainSeriousStolen))]
    internal static class CabinetStolenPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (__result)
                return;
            foreach (GameItem gun in Cabinets.DisplayedGuns())
            {
                if (gun.IsTag("STOLEN_TAG") && RNG.Roll(StolenHelper.GetFoundChance(gun)))
                {
                    StolenHelper.SetHeat(gun, 100);
                    __result = true;
                    return;
                }
            }
        }
    }

    // A cabinet put into the display case while a security officer is the customer (Cabinets.OnPutOnShow).
    [HarmonyPatch(typeof(GameItem), nameof(GameItem.OnAddedToShowcaseArea))]
    internal static class CabinetOnShowPatch
    {
        private static void Postfix(GameItem __instance)
        {
            try
            {
                if (Cabinets.IsCabinet(__instance))
                    Cabinets.OnPutOnShow(GraphUtils.FindAllChildrenType<GameItem>(__instance, Cabinets.IsGun, g => !Cabinets.IsGun(g)));
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("cabinet put on show: " + e.Message);
            }
        }
    }

    // A security officer's look at the display case on his visit (StoreClient.CompleteSecClient) goes over its top items
    // only; the guns in its cabinets count as well, in the game's order: contraband first, then stolen goods found. The
    // inspection itself (ContrabandHelper.StartInspection) already goes into containers (RecursiveInspect). VVittgenstein 2026-11-05
    // 13:05, current/008 quote 3 points 5 and 31.
    // The game's second look marks the stolen item it finds (StolenHelper.SetHeat 100); with contraband on top it stops before
    // that look. Contraband in a cabinet is therefore looked for before the game's method runs, which it then skips, so that a
    // stolen item on top is not marked either.
    [HarmonyPatch(typeof(ContrabandHelper), nameof(ContrabandHelper.SecurityCheckDisplayCase))]
    internal static class CabinetSecurityCheckPatch
    {
        private static bool Prefix(ref GameItem __result)
        {
            if (EmporiumEntry.Instance == null || EmporiumEntry.Instance.showcaseElement == null)
                return true;
            foreach (GameItem item in EmporiumEntry.Instance.showcaseElement.childItems)
            {
                if (item != null && item.IsTag("CONTRABAND_ITEM_TAG"))
                    return true;
            }
            foreach (GameItem gun in Cabinets.DisplayedGuns())
            {
                if (gun.IsTag("CONTRABAND_ITEM_TAG"))
                {
                    __result = gun;
                    return false;
                }
            }
            return true;
        }

        private static void Postfix(ref GameItem __result)
        {
            if (__result != null)
                return;
            foreach (GameItem gun in Cabinets.DisplayedGuns())
            {
                if (gun.IsTag("STOLEN_TAG") && StolenHelper.IsFound(gun))
                {
                    StolenHelper.SetHeat(gun, 100);
                    __result = gun;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(ScavHelper), nameof(ScavHelper.GetRandomScavengedItem), new[] { typeof(GameItem) }, new[] { ArgumentType.Out })]
    internal static class DumpCabinetPatch
    {
        private static void Postfix(List<GameItem> __result)
        {
            try
            {
                Cabinets.AddDumpFind(__result);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("dump cabinet: " + e.Message);
            }
        }
    }
}
