using System;
using System.Collections.Generic;
using UnityEngine;

namespace F5SnapshotSave
{
    // 0.8.0 (T-029, plan section 5 and 3.10): the first time in a game start that the objects of a mod make an F5 save leave
    // a waiting customer out of the snapshot or refuse the save, a window after the save notice names the mod and points
    // to the Workshop page of this mod, where the compatibility notes are linked. A mod is named once per game start. The
    // kept integrations (Pen_mod_pipette, vanilla_gun_expansion) count as well when the installed version differs from
    // the field table this mod ships; they do not follow the spec. CorpPresence is no longer an integration (T-029, yzz
    // 2026-11-12) and is named like any other mod that does not follow the spec.
    // The page is shown as text with a button that copies it: Application.OpenURL with an https address is the "process"
    // capability for the game's mod scan, which this mod does not declare.
    internal static class IncompatibleNotice
    {
        internal const string WorkshopPage = "https://steamcommunity.com/sharedfiles/filedetails/?id=3831061477";
        private const string WindowId = "f5snapshotsave_incompatible";
        // The mods named in a window since the game started.
        private static readonly HashSet<string> named = new HashSet<string>();

        internal static void After(List<string> mods)
        {
            if (mods == null || mods.Count == 0)
                return;
            var ids = new List<string>();
            foreach (string id in mods)
            {
                if (!string.IsNullOrEmpty(id) && id != F5SnapshotSaveMod.ModId && !named.Contains(id) && !ids.Contains(id))
                    ids.Add(id);
            }
            if (ids.Count == 0)
                return;
            var names = new List<string>();
            foreach (string id in ids)
                names.Add(Plain(DisplayName(id)));
            try
            {
                Show(names);
                foreach (string id in ids)
                    named.Add(id);
                Debug.Log(F5SnapshotSaveMod.LogPrefix + "incompatible mods named in a window: " + string.Join(", ", ids.ToArray()));
            }
            catch (Exception e)
            {
                Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "the window naming incompatible mods could not be shown: " + e);
            }
        }

        // The name in the mod's manifest, as the Mods panel shows it; the ID when there is none.
        internal static string DisplayName(string id)
        {
            try
            {
                if (ModLoader.Mods != null)
                {
                    foreach (LoadedMod mod in ModLoader.Mods)
                    {
                        if (mod != null && mod.Manifest != null && mod.Manifest.ID == id && !string.IsNullOrEmpty(mod.Manifest.Name))
                            return mod.Manifest.Name;
                    }
                }
            }
            catch (Exception)
            {
            }
            return id;
        }

        // A name is shown as written: rich text tags in it are not applied.
        private static string Plain(string name)
        {
            return name.IndexOf('<') < 0 ? name : "<noparse>" + name.Replace("</noparse>", "") + "</noparse>";
        }

        private static void Show(List<string> names)
        {
            CustomUIManager manager = CustomUIManager.Instance;
            if (manager == null)
                throw new InvalidOperationException("no CustomUIManager");
            if (manager.IsOpen(WindowId))
                manager.CloseWindow(WindowId);
            // The layer of this mod's settings windows (SaveSettingsControls.ShowQuestion): the store has a StoreUIManager.
            string layer = StoreUIManager.Instance != null ? "meta" : "menu";
            CustomUIBuilder b = manager.CreateWindow(WindowId, Text.IncompatibleTitle(), layer);
            b.SetSize(860f, 420f).Center().SetCloseOnEscape(true);
            b.AddLabel(Text.IncompatibleBody(names, WorkshopPage), "body");
            if (b.Last != null && b.Last.text != null)
                b.Last.text.enableWordWrapping = true;
            b.Last.SetPreferredSize(800f, 260f);
            b.AddSpace(8f);
            b.BeginRow(12f, TextAnchor.MiddleCenter);
            b.AddFlexibleSpace();
            b.AddButton(Text.CopyLinkButton(), CopyLink, "copy");
            b.Last.SetFixedWidth(240f);
            b.AddButton(Text.GotItButton(), Close, "ok");
            b.Last.SetFixedWidth(180f);
            b.AddFlexibleSpace();
            b.End();
            CustomUIWindow window = b.Show();
            window.BringToFront();
        }

        private static void CopyLink()
        {
            try
            {
                GUIUtility.systemCopyBuffer = WorkshopPage;
                Debug.Log(F5SnapshotSaveMod.LogPrefix + "copied the address of the Workshop page");
            }
            catch (Exception e)
            {
                Debug.LogWarning(F5SnapshotSaveMod.LogPrefix + "copying the address failed: " + e);
            }
        }

        private static void Close()
        {
            CustomUIManager manager = CustomUIManager.Instance;
            if (manager != null && manager.IsOpen(WindowId))
                manager.CloseWindow(WindowId);
        }
    }
}
