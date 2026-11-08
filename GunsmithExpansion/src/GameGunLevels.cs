using System;
using System.Collections.Generic;
using HarmonyLib;

namespace GunsmithExpansion
{
    // The game's own guns classed as this mod's guns are (final-strategy 121 and 127; VVittgenstein 2026-11-04 16:08,
    // 16:36): pistols and revolvers restricted I, SMGs and shotguns restricted II. The game
    // sets a gun's level when its directory makes it (ContrabandHelper.InitContrabandItem in GunsItemDirectory) and
    // DirectoryMaster.Item then lets the store's permits turn it from contraband into restricted
    // (PermitHelper.OnNewItemCreated: a permit covers the guns of exactly its level); the postfixes below set the new
    // level before that. A save keeps each gun's level, so after loading every owned gun still at the game's level is
    // set again: back to contraband if a permit had covered it (PermitHelper.RemoveRestrictedClass), the new level
    // (InitContrabandItem), then the store's permits (OnNewItemCreated). The guns' own values stay; the level moves
    // only the markups the game adds while a client negotiates for a gun of the store (GameItem.OnAddedToWeightedArea):
    // ContrabandHelper.ApplyContrabandFeature, 35, 70, 100% for contraband of levels 1 to 3, and under a permit
    // PermitHelper.ApplyRestrictedFeature, half of those (17, 35, 50%). The printed gun and the handmade pistol are
    // already level 1.
    internal static class GameGunLevels
    {
        internal sealed class GameGun
        {
            internal string Id;
            internal string ClassName;
            internal int GameLevel;
            internal int Level;
            internal int Caliber;
            internal int[] Stats;
        }

        // Identifier, this mod's class, the game's level, this mod's level, the caliber and the base stats in the order
        // of ModOrders.Stats (GunsItemDirectory, GunHelper.InitGun arguments read from the IL 2026-11-04).
        internal static readonly GameGun[] Guns =
        {
            new GameGun { Id = "printed_gun", ClassName = "手枪", GameLevel = 1, Level = 1, Caliber = 22, Stats = new[] { 15, 5, 15, 55, 40, 15 } },
            new GameGun { Id = "handmade_pistol", ClassName = "手枪", GameLevel = 1, Level = 1, Caliber = 22, Stats = new[] { 20, 15, 30, 65, 50, 30 } },
            new GameGun { Id = "heavy_handmade_pistol", ClassName = "手枪", GameLevel = 2, Level = 1, Caliber = 10, Stats = new[] { 30, 30, 34, 30, 40, 40 } },
            new GameGun { Id = "glock_receiver", ClassName = "手枪", GameLevel = 2, Level = 1, Caliber = 10, Stats = new[] { 30, 40, 42, 35, 50, 45 } },
            new GameGun { Id = "revolver", ClassName = "左轮（含左轮卡宾）", GameLevel = 2, Level = 1, Caliber = 45, Stats = new[] { 36, 50, 50, 38, 55, 52 } },
            new GameGun { Id = "smg", ClassName = "冲锋枪", GameLevel = 3, Level = 2, Caliber = 10, Stats = new[] { 65, 55, 65, 25, 60, 50 } },
            new GameGun { Id = "shotgun", ClassName = "霰弹枪", GameLevel = 3, Level = 2, Caliber = 12, Stats = new[] { 85, 75, 75, 30, 45, 50 } },
        };

        internal static GameGun Find(string id)
        {
            foreach (GameGun g in Guns)
            {
                if (g.Id == id)
                    return g;
            }
            return null;
        }

        // The directory sets the item's identifier only after its creator returns (Directory.Create), so the patches
        // name their gun.
        internal static void OnCreated(GameItem gun, string id)
        {
            GameGun g = Find(id);
            if (gun != null && g != null && g.GameLevel != g.Level)
                ContrabandHelper.InitContrabandItem(gun, g.Level);
        }

        internal static int LevelOf(GameItem gun)
        {
            int level = ContrabandHelper.GetRestrictedLevel(gun);
            return level != 0 ? level : ContrabandHelper.GetContrabandLevel(gun);
        }

        internal static void OnGameLoaded()
        {
            try
            {
                if (EmporiumEntry.Instance == null)
                    return;
                int changed = 0;
                foreach (GameItem item in EmporiumEntry.Instance.GetAllOwnedItems(false, true))
                {
                    GameGun g = item != null ? Find(item.identifier) : null;
                    if (g == null || g.GameLevel == g.Level || LevelOf(item) != g.GameLevel)
                        continue;
                    PermitHelper.RemoveRestrictedClass(item);
                    ContrabandHelper.InitContrabandItem(item, g.Level);
                    PermitHelper.OnNewItemCreated(item);
                    changed++;
                }
                if (changed > 0)
                    GunsmithMod.Log("game guns set to this mod's levels after loading: " + changed);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("game gun levels on load: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(GunsItemDirectory), "CreateHeavyHandmadePistol")]
    internal static class HeavyHandmadePistolLevelPatch
    {
        private static void Postfix(GameItem __result)
        {
            GameGunLevels.OnCreated(__result, "heavy_handmade_pistol");
        }
    }

    // The game's frame of the heavy handmade pistol (Ironcraft 10 Frame) is contraband of the gun's game level, 2 (GunFrameHelper.InitFrame in
    // GunsItemDirectory.CreateHeavyHandmadePistolFrame); here it takes the gun's level, 1 (VVittgenstein 2026-11-07 20:16, current/T-006 quote 21). Frames
    // saved before 0.7 keep the level they were saved with (quote 22). The game's only other frame, the handmade pistol's, is already level 1.
    [HarmonyPatch(typeof(GunsItemDirectory), "CreateHeavyHandmadePistolFrame")]
    internal static class HeavyHandmadePistolFrameLevelPatch
    {
        private static void Postfix(GameItem __result)
        {
            GameGunLevels.OnCreated(__result, "heavy_handmade_pistol");
        }
    }

    [HarmonyPatch(typeof(GunsItemDirectory), "CreateGlockReceiver")]
    internal static class GlockReceiverLevelPatch
    {
        private static void Postfix(GameItem __result)
        {
            GameGunLevels.OnCreated(__result, "glock_receiver");
        }
    }

    [HarmonyPatch(typeof(GunsItemDirectory), "CreateRevolver")]
    internal static class RevolverLevelPatch
    {
        private static void Postfix(GameItem __result)
        {
            GameGunLevels.OnCreated(__result, "revolver");
        }
    }

    [HarmonyPatch(typeof(GunsItemDirectory), "CreateSMG")]
    internal static class SmgLevelPatch
    {
        private static void Postfix(GameItem __result)
        {
            GameGunLevels.OnCreated(__result, "smg");
        }
    }

    [HarmonyPatch(typeof(GunsItemDirectory), "CreateShotgun")]
    internal static class ShotgunLevelPatch
    {
        private static void Postfix(GameItem __result)
        {
            GameGunLevels.OnCreated(__result, "shotgun");
        }
    }
}
