using System;
using System.Collections.Generic;
using UnityEngine;

namespace GunsmithExpansion
{
    // A point on a gun's picture where a gun mod's mounted picture meets the gun, in picture pixels (x right, y down).
    internal struct MountPoint
    {
        internal readonly int X;
        internal readonly int Y;

        internal MountPoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    // The mount points of one gun (data/mounts.tsv, generated into Mounts.cs); null where the gun has no point for a
    // slot, for example a stock that is part of the gun's own picture.
    internal sealed class MountPoints
    {
        internal readonly bool Pistol;
        internal readonly MountPoint?[] Points;
        // The gun picture's scale step (k0 = 1.2 pixels per centimetre, each step 1.2 times the last; data/gun-steps.tsv)
        internal readonly string Step;
        // The first and last column of the gun's top rail around the rail point (data/rail-spans.tsv); -1 when unknown
        internal readonly int RailX0;
        internal readonly int RailX1;
        // One of the game's own guns, drawn seen from a little above: gun mods draw their _vanilla pictures on it
        // (VVittgenstein 2026-11-03, current/005 quotes 90 and 91)
        internal readonly bool Vanilla;

        internal MountPoints(bool pistol, MountPoint? stock, MountPoint? rail, MountPoint? barrel, MountPoint? magazine,
            MountPoint? under, string step, int railX0 = -1, int railX1 = -1, bool vanilla = false)
        {
            Pistol = pistol;
            Points = new[] { stock, rail, barrel, magazine, under };
            Step = step;
            RailX0 = railX0;
            RailX1 = railX1;
            Vanilla = vanilla;
        }
    }

    // The drawn part of a mounted picture, in picture pixels (y down; X1 and Y1 exclusive), for a muzzle device the
    // row on the bore line: the middle row, or the row data/view-anchors.tsv gives (an eccentric suppressor), for a
    // sight its feet on the bottom row (a scope's rings or a sight's base), and for a magazine its top 2 rows (the part
    // that goes into the well); feet and top as first and last column, -1 when none. A muzzle device's column on the
    // barrel point (the column after the muzzle) is its first drawn column, or the next one for a device that starts
    // one column into the muzzle (data/muzzle-overlap.tsv).
    // Spans stores compile-time 4-byte tuples (y0, y1, rx0, rx1) of inclusive sprite-pixel indices covering every drawn
    // pixel (alpha > 1), pre-merged for 16 px cell occupancy so Mounting.Shape needs zero Texture2D.GetPixel calls.
    internal struct ViewBox
    {
        internal readonly int X0;
        internal readonly int Y0;
        internal readonly int X1;
        internal readonly int Y1;
        internal readonly int BoreRow;
        internal readonly int FeetX0;
        internal readonly int FeetX1;
        internal readonly int MuzzleX;
        // The row on the gun's top (a sight) or underside (an under-rail part) where the picture has parts beside the
        // gun, as the game's makeshift red dot sight and toy laser pointer do (data/view-attach.tsv); -1: its bottom or
        // top row.
        internal readonly int AttachRow;
        internal readonly byte[] Spans;

        internal ViewBox(int x0, int y0, int x1, int y1, int boreRow = -1, int feetX0 = -1, int feetX1 = -1,
            int muzzleX = -1, int attachRow = -1, byte[] spans = null)
        {
            AttachRow = attachRow;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            BoreRow = boreRow >= 0 ? boreRow : (y0 + y1 - 1) / 2;
            FeetX0 = feetX0;
            FeetX1 = feetX1;
            MuzzleX = muzzleX >= 0 ? muzzleX : x0;
            Spans = spans;
        }
    }

    // Draws this mod's gun mods on the guns they are attached to, the way GunModDirectory draws the game's own:
    // parentItemSpriteModifierForItemFunc gives the mounted picture and its position (top left corner from the gun
    // picture's top left corner, y up), parentItemShapeModifierForItemFunc adds the grid cells the picture covers. A
    // gun mod's mounted pictures are Textures\Items\<mod id>_view_long.png (on long guns) and _view_pistol.png (on
    // pistols), and the versions drawn for one scale step of the gun pictures (<mod id>_view_long_k1.png and the like;
    // current/005 section 16.4): a gun takes the version of its step, or the first one where its step has none. The position follows from the gun's mount point for the mod's slot and the drawn part of the picture,
    // with the offsets measured on the game's guns (tools/mount_points.py): stocks go 2 pixels into the gun's rear,
    // sights sit on the gun's top with their feet on its rail (OnRail), muzzle devices start after the muzzle with their bore row (ViewBox.BoreRow) on the
    // barrel's line, magazines go 2 pixels
    // into the well with the middle of their top on the point (a base plate of at most 3 pixels less), under-rail parts hang from the gun's bottom (centred on
    // long guns, starting at the trigger guard on pistols). Mods of the special slot, mods without a mounted picture
    // and guns without a point for the slot draw nothing, as the game's special-slot mods do.
    internal static class Mounting
    {
        private enum Slot
        {
            Stock,
            Rail,
            Barrel,
            Magazine,
            Under,
        }

        private const int Cell = 16;
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private static readonly int[] NeighborDx = { 1, -1, 0, 0 };
        private static readonly int[] NeighborDy = { 0, 0, 1, -1 };

        internal static void Enable(GameItem mod, ModDef d)
        {
            Slot slot;
            if (!TrySlot(d.Slot, out slot))
                return;
            string id = d.Id;
            mod.parentItemSpriteModifierForItemFunc = (item, inventory, marker, shape, tags, parent) => Sprites(id, slot, parent);
            mod.parentItemShapeModifierForItemFunc = (item, inventory, marker, shape, tags, parent) => Shape(id, slot, parent);
        }

        // One of the game's gun mods (VanillaMods): drawn this way on this mod's guns, with the pictures named after
        // `view`; the game's own functions keep drawing it on the game's guns.
        internal static void EnableOnModGuns(GameItem mod, string slotName, string view)
        {
            Slot slot;
            if (!TrySlot(slotName, out slot))
                return;
            var gameSprites = mod.parentItemSpriteModifierForItemFunc;
            var gameShape = mod.parentItemShapeModifierForItemFunc;
            mod.parentItemSpriteModifierForItemFunc = (item, inventory, marker, shape, tags, parent) =>
                IsModGun(parent) ? Sprites(view, slot, parent)
                : gameSprites != null ? gameSprites(item, inventory, marker, shape, tags, parent) : new List<ItemSpriteModifier>();
            mod.parentItemShapeModifierForItemFunc = (item, inventory, marker, shape, tags, parent) =>
                IsModGun(parent) ? Shape(view, slot, parent)
                : gameShape != null ? gameShape(item, inventory, marker, shape, tags, parent) : null;
        }

        private static bool IsModGun(GameItem parent)
        {
            MountPoints gun;
            return parent != null && parent.identifier != null && Mounts.Guns.TryGetValue(parent.identifier, out gun) && !gun.Vanilla;
        }

        private static bool TrySlot(string name, out Slot slot)
        {
            slot = Slot.Stock;
            switch (name)
            {
                case "stock": slot = Slot.Stock; return true;
                case "rail": slot = Slot.Rail; return true;
                case "barrel":
                case "barrel_suppressor": slot = Slot.Barrel; return true;
                case "magazine": slot = Slot.Magazine; return true;
                case "under": slot = Slot.Under; return true;
                default: return false;
            }
        }

        private static List<ItemSpriteModifier> Sprites(string id, Slot slot, GameItem parent)
        {
            var list = new List<ItemSpriteModifier>();
            Sprite sprite;
            Vector2Int topLeft;
            ViewBox box;
            if (Place(id, slot, parent, out sprite, out topLeft, out box))
                list.Add(new ItemSpriteModifier(sprite, new Vector2(topLeft.x, -topLeft.y)));
            return list;
        }

        // Computes the exact 16x16 grid cells covered by drawn pixels of the mounted picture in gun-local cell
        // coordinates (including cy < 0 above the gun top). Empty bounding-box corners remain 0; if any mod cell is
        // not 4-connected to parent.shape, a minimal orthogonal bridge is added so the merged modifiedShape stays
        // 4-connected.
        private static GridShapeBuilder Shape(string id, Slot slot, GameItem parent)
        {
            Sprite sprite;
            Vector2Int topLeft;
            ViewBox box;
            if (!Place(id, slot, parent, out sprite, out topLeft, out box))
                return null;

            int trimTopPx = ItemShapes.GetTrimTop(parent) * Cell;
            int gridTopY = topLeft.y - trimTopPx;
            int minCx, maxCx, minCy, maxCy;
            byte[] mask;
            int w, h;

            byte[] spans = box.Spans;
            if (spans != null && spans.Length >= 4)
            {
                minCx = int.MaxValue;
                maxCx = int.MinValue;
                minCy = int.MaxValue;
                maxCy = int.MinValue;
                for (int i = 0; i + 3 < spans.Length; i += 4)
                {
                    int bcx0 = Floor(topLeft.x + spans[i + 2]);
                    int bcx1 = Floor(topLeft.x + spans[i + 3]);
                    int bcy0 = Floor(gridTopY + spans[i]);
                    int bcy1 = Floor(gridTopY + spans[i + 1]);
                    if (bcx0 < minCx) minCx = bcx0;
                    if (bcx1 > maxCx) maxCx = bcx1;
                    if (bcy0 < minCy) minCy = bcy0;
                    if (bcy1 > maxCy) maxCy = bcy1;
                }
                if (maxCx < minCx || maxCy < minCy)
                    return null;

                w = maxCx - minCx + 1;
                h = maxCy - minCy + 1;
                mask = new byte[w * h];
                for (int i = 0; i + 3 < spans.Length; i += 4)
                {
                    int bcx0 = Floor(topLeft.x + spans[i + 2]);
                    int bcx1 = Floor(topLeft.x + spans[i + 3]);
                    int bcy0 = Floor(gridTopY + spans[i]);
                    int bcy1 = Floor(gridTopY + spans[i + 1]);
                    for (int cy = bcy0; cy <= bcy1; cy++)
                    {
                        int rowOffset = (cy - minCy) * w;
                        for (int cx = bcx0; cx <= bcx1; cx++)
                            mask[rowOffset + (cx - minCx)] = 1;
                    }
                }
            }
            else
            {
                minCx = Floor(topLeft.x + box.X0);
                maxCx = Floor(topLeft.x + box.X1 - 1);
                minCy = Floor(gridTopY + box.Y0);
                maxCy = Floor(gridTopY + box.Y1 - 1);
                if (maxCx < minCx || maxCy < minCy)
                    return null;
                w = maxCx - minCx + 1;
                h = maxCy - minCy + 1;
                mask = new byte[w * h];
                for (int i = 0; i < mask.Length; i++)
                    mask[i] = 1;
            }

            if (parent != null && parent.shape != null)
                EnsureFourConnected(ref mask, ref minCx, ref minCy, ref w, ref h, parent.shape);

            return new GridShapeBuilder().SetData(mask, w).SetTransform(minCx, minCy, false, 0);
        }

        // Guarantees every occupied cell in the mod mask is 4-connected to parentShape.
        // Fast-paths without reallocating when all mod cells are already reachable via 4-neighbors from parentShape.
        private static void EnsureFourConnected(ref byte[] mask, ref int minCx, ref int minCy, ref int w, ref int h,
            GridShape parentShape)
        {
            int pw = parentShape.width;
            int ph = parentShape.height;
            if (pw <= 0 || ph <= 0)
                return;

            var parentCells = new HashSet<long>();
            var reached = new HashSet<long>();
            var queue = new Queue<long>();
            for (int py = 0; py < ph; py++)
            {
                for (int px = 0; px < pw; px++)
                {
                    if (parentShape.GetLocal(px, py) != 0)
                    {
                        long k = PackCell(px, py);
                        parentCells.Add(k);
                        reached.Add(k);
                        queue.Enqueue(k);
                    }
                }
            }
            if (parentCells.Count == 0)
                return;

            var modCells = new HashSet<long>();
            for (int dy = 0; dy < h; dy++)
            {
                int row = dy * w;
                for (int dx = 0; dx < w; dx++)
                {
                    if (mask[row + dx] != 0)
                        modCells.Add(PackCell(minCx + dx, minCy + dy));
                }
            }
            if (modCells.Count == 0)
                return;

            FloodIntoMod(queue, reached, modCells);

            var unreached = new List<long>();
            foreach (long k in modCells)
            {
                if (!reached.Contains(k))
                    unreached.Add(k);
            }
            if (unreached.Count == 0)
                return;

            int maxCx = minCx + w - 1;
            int maxCy = minCy + h - 1;
            while (unreached.Count > 0)
            {
                int bestDist = int.MaxValue;
                int bestAx = 0, bestAy = 0, bestBx = 0, bestBy = 0;
                foreach (long rk in reached)
                {
                    int ax, ay;
                    UnpackCell(rk, out ax, out ay);
                    for (int i = 0; i < unreached.Count; i++)
                    {
                        int bx, by;
                        UnpackCell(unreached[i], out bx, out by);
                        int dist = Math.Abs(ax - bx) + Math.Abs(ay - by);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestAx = ax;
                            bestAy = ay;
                            bestBx = bx;
                            bestBy = by;
                            if (bestDist <= 2)
                                break;
                        }
                    }
                    if (bestDist <= 2)
                        break;
                }

                // Prefer stepping along the axis that keeps intermediate bridge cells inside the existing mod bounds.
                bool stepXFirst = bestBx >= minCx && bestBx <= maxCx && bestAy >= minCy && bestAy <= maxCy;
                int cx = bestAx;
                int cy = bestAy;
                if (stepXFirst)
                {
                    while (cx != bestBx)
                    {
                        cx += cx < bestBx ? 1 : -1;
                        AddBridgeCell(cx, cy, modCells, reached, queue, ref minCx, ref maxCx, ref minCy, ref maxCy);
                    }
                    while (cy != bestBy)
                    {
                        cy += cy < bestBy ? 1 : -1;
                        AddBridgeCell(cx, cy, modCells, reached, queue, ref minCx, ref maxCx, ref minCy, ref maxCy);
                    }
                }
                else
                {
                    while (cy != bestBy)
                    {
                        cy += cy < bestBy ? 1 : -1;
                        AddBridgeCell(cx, cy, modCells, reached, queue, ref minCx, ref maxCx, ref minCy, ref maxCy);
                    }
                    while (cx != bestBx)
                    {
                        cx += cx < bestBx ? 1 : -1;
                        AddBridgeCell(cx, cy, modCells, reached, queue, ref minCx, ref maxCx, ref minCy, ref maxCy);
                    }
                }

                FloodIntoMod(queue, reached, modCells);
                unreached.RemoveAll(k => reached.Contains(k));
            }

            w = maxCx - minCx + 1;
            h = maxCy - minCy + 1;
            mask = new byte[w * h];
            foreach (long k in modCells)
            {
                int cx, cy;
                UnpackCell(k, out cx, out cy);
                mask[(cy - minCy) * w + (cx - minCx)] = 1;
            }
        }

        private static void FloodIntoMod(Queue<long> queue, HashSet<long> reached, HashSet<long> modCells)
        {
            while (queue.Count > 0)
            {
                int x, y;
                UnpackCell(queue.Dequeue(), out x, out y);
                for (int d = 0; d < 4; d++)
                {
                    long nk = PackCell(x + NeighborDx[d], y + NeighborDy[d]);
                    if (modCells.Contains(nk) && reached.Add(nk))
                        queue.Enqueue(nk);
                }
            }
        }

        private static void AddBridgeCell(int cx, int cy, HashSet<long> modCells, HashSet<long> reached, Queue<long> queue,
            ref int minCx, ref int maxCx, ref int minCy, ref int maxCy)
        {
            long k = PackCell(cx, cy);
            modCells.Add(k);
            if (cx < minCx) minCx = cx;
            if (cx > maxCx) maxCx = cx;
            if (cy < minCy) minCy = cy;
            if (cy > maxCy) maxCy = cy;
            if (reached.Add(k))
                queue.Enqueue(k);
        }

        private static long PackCell(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        private static void UnpackCell(long packed, out int x, out int y)
        {
            x = (int)(packed >> 32);
            y = (int)(packed & 0xFFFFFFFFL);
        }

        private static int Floor(int pixels)
        {
            return (int)Math.Floor(pixels / (double)Cell);
        }

        // Where the mounted picture of mod `id` goes on `parent`: its top left corner in the gun picture's pixels.
        private static bool Place(string id, Slot slot, GameItem parent, out Sprite sprite, out Vector2Int topLeft,
            out ViewBox box)
        {
            sprite = null;
            topLeft = default(Vector2Int);
            box = default(ViewBox);
            MountPoints gun;
            if (parent == null || parent.identifier == null || !Mounts.Guns.TryGetValue(parent.identifier, out gun))
                return false;
            MountPoint? p = gun.Points[(int)slot];
            string key = ViewKey(id, gun, parent.identifier);
            if (p == null || !Mounts.Views.TryGetValue(key, out box))
                return false;
            sprite = ModSprite(key);
            if (sprite == null)
                return false;
            int x = p.Value.X, y = p.Value.Y;
            int midX = (box.X0 + box.X1) / 2;
            switch (slot)
            {
                case Slot.Stock:
                    topLeft = new Vector2Int(x + 2 - box.X1, y - box.Y0);
                    break;
                case Slot.Rail:
                    topLeft = new Vector2Int(OnRail(x - midX, box, gun), box.AttachRow >= 0 ? y - box.AttachRow : y + 1 - box.Y1);
                    break;
                case Slot.Barrel:
                    topLeft = new Vector2Int(x - box.MuzzleX, y - box.BoreRow);
                    break;
                case Slot.Magazine:
                {
                    // 2 pixels into the well; a picture of at most 3 pixels (the base plate under a pistol grip, the
                    // magazine body hidden in the grip) goes in by its height less 2, so it stays below the grip. The
                    // middle of the top 2 rows (the part in the well) goes on the point, the middle of the well
                    // (tools/align_magazines.py): a curved magazine's bottom reaches further forward (VVittgenstein 2026-11-03,
                    // current/005 quotes 101 and 102)
                    int topMid = box.FeetX0 >= 0 ? (box.FeetX0 + box.FeetX1 + 1) / 2 : midX;
                    topLeft = new Vector2Int(x - topMid, y - Math.Min(2, Math.Max(0, box.Y1 - box.Y0 - 2)) - box.Y0);
                    break;
                }
                default:
                    // An under-rail picture with the columns of its mount (FeetX0 and FeetX1, data/under-anchors.tsv) has the middle of
                    // them on the point, on a pistol their first column (T-035's assembly, final-strategy 347: a laser's mount block
                    // and a bipod's clamp stood behind the rail they hang from); the others as before.
                    topLeft = new Vector2Int(gun.Pistol ? x - (box.FeetX0 >= 0 ? box.FeetX0 : box.X0) : x - (box.FeetX0 >= 0 ? (box.FeetX0 + box.FeetX1 + 1) / 2 : midX),
                        y - 1 - (box.AttachRow >= 0 ? box.AttachRow : box.Y0));
                    break;
            }
            return true;
        }

        // A sight centred on the rail point (picture column `left`) whose feet stand off the gun's top rail moves along
        // the gun as little as it takes to stand on it, at least one pixel in from the rail's ends; on a rail shorter
        // than the feet they are centred on it. VVittgenstein 2026-11-02 (current/005 quote 44): scopes stood with their rear
        // ring in the air behind the rail.
        internal static int OnRail(int left, ViewBox box, MountPoints gun)
        {
            if (box.FeetX0 < 0 || gun.RailX0 < 0)
                return left;
            int f0 = left + box.FeetX0, f1 = left + box.FeetX1;
            int lo = gun.RailX0 + 1, hi = gun.RailX1 - 1;
            if (f1 - f0 > hi - lo)
                return left + (lo + hi - f0 - f1) / 2;
            if (f0 < lo)
                return left + lo - f0;
            if (f1 > hi)
                return left - (f1 - hi);
            return left;
        }

        // The mounted picture of gun mod `id` used on `gun` (identifier `gunId`): on the game's own guns the version drawn
        // the way the game draws its guns (_vanilla); else the version drawn for the gun's scale step, else the first. A
        // muzzle device that would stand half a pixel off the gun's barrel or be less high than it takes its taller
        // version (Mounts.Variants, tools/muzzle_variants.py; VVittgenstein 2026-11-03, current/005 quotes 102 and 103).
        internal static string ViewKey(string id, MountPoints gun, string gunId)
        {
            string key = id + (gun.Pistol ? "_view_pistol" : "_view_long");
            if (gun.Vanilla && Mounts.Views.ContainsKey(key + "_vanilla"))
                key += "_vanilla";
            else if (gun.Step != null && Mounts.Views.ContainsKey(key + "_" + gun.Step))
                key += "_" + gun.Step;
            string taller;
            return gunId != null && Mounts.Variants.TryGetValue(gunId + "|" + key, out taller) ? taller : key;
        }

        private static Sprite ModSprite(string key)
        {
            Sprite sprite;
            if (sprites.TryGetValue(key, out sprite))
                return sprite;
            LoadedMod mod = ModLoader.Mods.Find(m => m.Instance is GunsmithMod);
            if (mod == null || mod.Sprites == null || !mod.Sprites.TryGetValue(key, out sprite))
                sprite = null;
            sprites[key] = sprite;
            return sprite;
        }
    }
}
