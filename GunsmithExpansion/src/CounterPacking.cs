using System;
using System.Collections.Generic;

namespace GunsmithExpansion
{
    // Coordinates returned by this class belong to the destination grid. Planning never changes live items.
    internal static class CounterPacking
    {
        internal static GridShape Move(GridShape s, int dx, int dy)
        {
            return dx == 0 && dy == 0 ? s :
                new GridShapeBuilder(s).SetTransform(s.minX + dx, s.minY + dy, s.flipped, s.orientation).Build();
        }

        // GameGridInventory tests occupied bytes (1), including rotated and flipped sparse shapes.
        private static bool Mark(int[] board, int width, GridShape shape)
        {
            for (int y = shape.minY; y <= shape.maxY; y++)
                for (int x = shape.minX; x <= shape.maxX; x++)
                {
                    if (shape.Get(x, y) != 1)
                        continue;
                    if (x < 0 || x >= width || y < 0 || y >= board.Length || (board[y] & (1 << x)) != 0)
                        return false;
                    board[y] |= 1 << x;
                }
            return true;
        }

        internal static GridShape[] Translate(GridShape[] source, int width, int height, int dx, int dy)
        {
            var dest = new GridShape[source.Length];
            var board = new int[height];
            for (int i = 0; i < source.Length; i++)
            {
                if (source[i] == null)
                    return null;
                dest[i] = Move(source[i], dx, dy);
                if (!Mark(board, width, dest[i]))
                    return null;
            }
            return dest;
        }

        private sealed class Candidate
        {
            internal GridShape Shape;
            internal int[] Rows;
            internal bool Preferred;
        }

        private sealed class Piece
        {
            internal int Index;
            internal int Cells;
            internal readonly List<Candidate> Choices = new List<Candidate>();
        }

        // A saved layout can already use the destination grid's coordinates.
        internal static GridShape[] Recover(GridShape[] source, int width, int height, int dx, int dy)
        {
            return Translate(source, width, height, 0, 0) ?? Repack(source, width, height, dx, dy);
        }

        // Keep the bottom subrectangle fixed first. Relax those positions only if necessary.
        internal static GridShape[] Repack(GridShape[] source, int width, int height, int dx, int dy)
        {
            GridShape[] shifted = Translate(source, width, height, -dx, -dy);
            if (shifted != null)
                return shifted;

            var pieces = new List<Piece>();
            int total = 0;
            for (int i = 0; i < source.Length; i++)
            {
                GridShape s = source[i];
                if (s == null)
                    return null;
                Piece piece = MakePiece(i, s, width, height, dx, dy);
                if (piece.Choices.Count == 0)
                    return null;
                total += piece.Cells;
                if (total > width * height)
                    return null;
                pieces.Add(piece);
            }
            var result = new GridShape[source.Length];
            foreach (bool pin in new[] { true, false })
            {
                pieces.Sort((a, b) =>
                {
                    if (pin && a.Choices[0].Preferred != b.Choices[0].Preferred)
                        return a.Choices[0].Preferred ? -1 : 1;
                    if (a.Cells != b.Cells) return b.Cells.CompareTo(a.Cells);
                    int count = a.Choices.Count.CompareTo(b.Choices.Count);
                    return count != 0 ? count : a.Index.CompareTo(b.Index);
                });
                if (Search(pieces, 0, new int[height], result, pin, new HashSet<string>()))
                    return result;
            }
            return null;
        }

        private static Piece MakePiece(int index, GridShape s, int width, int height, int dx, int dy)
        {
            var piece = new Piece { Index = index };
            for (int y = 0; y < s.height; y++)
                for (int x = 0; x < s.width; x++)
                    if (s.GetLocal(x, y) == 1)
                        piece.Cells++;

            var seen = new HashSet<string>();
            var probe = new GridShapeBuilder(s);
            GridShape oriented = probe;
            for (int flip = 0; flip < 2; flip++)
                for (int rot = 0; rot < 4; rot++)
                {
                    probe.SetTransform(0, 0, flip == 0 ? s.flipped : !s.flipped, (s.orientation + rot) % 4);
                    // Empty margin cells may legally extend outside a grid. Enumerate the occupied bounds.
                    int minX = probe.globalWidth, minY = probe.globalHeight, maxX = -1, maxY = -1;
                    for (int y = 0; y < probe.globalHeight; y++)
                        for (int x = 0; x < probe.globalWidth; x++)
                            if (oriented.Get(x, y) == 1)
                            {
                                minX = Math.Min(minX, x);
                                minY = Math.Min(minY, y);
                                maxX = Math.Max(maxX, x);
                                maxY = Math.Max(maxY, y);
                            }
                    if (maxX < 0)
                    {
                        piece.Choices.Add(new Candidate { Shape = Move(s, -dx, -dy), Rows = new int[height], Preferred = true });
                        return piece;
                    }
                    for (int y = height - 1 - maxY; y >= -minY; y--)
                        for (int x = -minX; x < width - maxX; x++)
                        {
                            probe.SetPosition(x, y);
                            var rows = new int[height];
                            if (!Mark(rows, width, probe) || !seen.Add(string.Join(",", rows)))
                                continue;
                            bool preferred = x == s.minX - dx && y == s.minY - dy && flip == 0 && rot == 0;
                            var c = new Candidate { Shape = probe.Build(), Rows = rows, Preferred = preferred };
                            if (preferred) piece.Choices.Insert(0, c);
                            else piece.Choices.Add(c);
                        }
                }
            return piece;
        }

        private static bool Search(List<Piece> pieces, int index, int[] board, GridShape[] result, bool pin, HashSet<string> failed)
        {
            if (index == pieces.Count)
                return true;
            // With a fixed piece order, these rows and this index identify the entire remaining problem.
            string key = index + ":" + string.Join(",", board);
            if (failed.Contains(key)) return false;
            Piece piece = pieces[index];
            int count = pin && piece.Choices[0].Preferred ? 1 : piece.Choices.Count;
            for (int i = 0; i < count; i++)
            {
                Candidate c = piece.Choices[i];
                bool fits = true;
                for (int y = 0; y < board.Length && fits; y++)
                    fits = (board[y] & c.Rows[y]) == 0;
                if (!fits) continue;
                for (int y = 0; y < board.Length; y++)
                    board[y] |= c.Rows[y];
                result[piece.Index] = c.Shape;
                if (Search(pieces, index + 1, board, result, pin, failed))
                    return true;
                for (int y = 0; y < board.Length; y++)
                    board[y] ^= c.Rows[y];
            }
            // Bound the cache without truncating the search or accepting a partial arrangement.
            if (failed.Count < 32768) failed.Add(key);
            return false;
        }
    }
}
