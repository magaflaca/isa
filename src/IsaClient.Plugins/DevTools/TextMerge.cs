using System;
using System.Collections.Generic;
using System.Linq;

namespace IsaClient.Plugins.DevTools;

/// <summary>
/// A three-way merge of text by lines, like git's: the changes "ours" made to the base and the changes "theirs" made to
/// it go into one text; where both changed the same lines differently it is a conflict (and nothing is merged).
/// Used to put a TerraAngel fork's changes into the pack's adapted copy of the same file.
/// </summary>
public static class TextMerge
{
    private readonly struct Hunk
    {
        public readonly int BaseStart;

        public readonly int BaseEnd;

        public readonly string[] Lines;

        public Hunk(int baseStart, int baseEnd, string[] lines)
        {
            BaseStart = baseStart;
            BaseEnd = baseEnd;
            Lines = lines;
        }
    }

    /// <summary>The merged text, or null when the two sides changed the same place differently.</summary>
    public static string Merge(string baseText, string ours, string theirs)
    {
        string[] b = Lines(baseText);
        string[] o = Lines(ours);
        string[] t = Lines(theirs);
        List<Hunk> mine = Diff(b, o);
        List<Hunk> other = Diff(b, t);
        List<string> result = new List<string>();
        int at = 0;
        int i = 0;
        int j = 0;
        while (i < mine.Count || j < other.Count)
        {
            // The next group: hunks of both sides that overlap or touch (touching counts: safer than guessing).
            int start = Math.Min(i < mine.Count ? mine[i].BaseStart : int.MaxValue, j < other.Count ? other[j].BaseStart : int.MaxValue);
            int end = start;
            int i2 = i;
            int j2 = j;
            bool grew = true;
            while (grew)
            {
                grew = false;
                while (i2 < mine.Count && mine[i2].BaseStart <= end)
                {
                    end = Math.Max(end, mine[i2].BaseEnd);
                    i2++;
                    grew = true;
                }
                while (j2 < other.Count && other[j2].BaseStart <= end)
                {
                    end = Math.Max(end, other[j2].BaseEnd);
                    j2++;
                    grew = true;
                }
            }
            for (; at < start; at++)
            {
                result.Add(b[at]);
            }
            List<string> mineText = Apply(b, mine, i, i2, start, end);
            List<string> otherText = Apply(b, other, j, j2, start, end);
            if (i2 > i && j2 > j && !mineText.SequenceEqual(otherText))
            {
                return null;
            }
            result.AddRange((i2 > i) ? mineText : otherText);
            at = end;
            i = i2;
            j = j2;
        }
        for (; at < b.Length; at++)
        {
            result.Add(b[at]);
        }
        return string.Join("\n", result);
    }

    /// <summary>The base lines start..end with the hunks from..to applied.</summary>
    private static List<string> Apply(string[] b, List<Hunk> hunks, int from, int to, int start, int end)
    {
        List<string> text = new List<string>();
        int at = start;
        for (int k = from; k < to; k++)
        {
            for (; at < hunks[k].BaseStart; at++)
            {
                text.Add(b[at]);
            }
            text.AddRange(hunks[k].Lines);
            at = hunks[k].BaseEnd;
        }
        for (; at < end; at++)
        {
            text.Add(b[at]);
        }
        return text;
    }

    private static string[] Lines(string text)
    {
        return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    /// <summary>What changed from <paramref name="a"/> to <paramref name="b"/>: base ranges and the lines they became.</summary>
    private static List<Hunk> Diff(string[] a, string[] b)
    {
        // Same start and end are skipped; the middle is compared with a longest common subsequence.
        int prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix])
        {
            prefix++;
        }
        int suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix])
        {
            suffix++;
        }
        int n = a.Length - prefix - suffix;
        int m = b.Length - prefix - suffix;
        List<Hunk> hunks = new List<Hunk>();
        if (n == 0 && m == 0)
        {
            return hunks;
        }
        if (n == 0 || m == 0 || (long)n * m > 30_000_000L)
        {
            // Nothing in common to look for (or too big to compare line by line): one change.
            hunks.Add(new Hunk(prefix, prefix + n, b.Skip(prefix).Take(m).ToArray()));
            return hunks;
        }
        int[] ah = new int[n];
        int[] bh = new int[m];
        Dictionary<string, int> ids = new Dictionary<string, int>();
        for (int k = 0; k < n; k++)
        {
            ah[k] = Id(ids, a[prefix + k]);
        }
        for (int k = 0; k < m; k++)
        {
            bh[k] = Id(ids, b[prefix + k]);
        }
        // lcs[x, y] = longest common subsequence of a[x..] and b[y..].
        int[,] lcs = new int[n + 1, m + 1];
        for (int x = n - 1; x >= 0; x--)
        {
            for (int y = m - 1; y >= 0; y--)
            {
                lcs[x, y] = (ah[x] == bh[y]) ? (lcs[x + 1, y + 1] + 1) : Math.Max(lcs[x + 1, y], lcs[x, y + 1]);
            }
        }
        int xi = 0;
        int yi = 0;
        int hunkA = -1;
        int hunkB = -1;
        void Close()
        {
            if (hunkA >= 0)
            {
                hunks.Add(new Hunk(prefix + hunkA, prefix + xi, b.Skip(prefix + hunkB).Take(yi - hunkB).ToArray()));
                hunkA = -1;
            }
        }
        while (xi < n || yi < m)
        {
            if (xi < n && yi < m && ah[xi] == bh[yi])
            {
                Close();
                xi++;
                yi++;
                continue;
            }
            if (hunkA < 0)
            {
                hunkA = xi;
                hunkB = yi;
            }
            if (yi < m && (xi == n || lcs[xi, yi + 1] >= lcs[xi + 1, yi]))
            {
                yi++;
            }
            else
            {
                xi++;
            }
        }
        Close();
        return hunks;
    }

    private static int Id(Dictionary<string, int> ids, string line)
    {
        if (!ids.TryGetValue(line, out int id))
        {
            id = ids.Count;
            ids[line] = id;
        }
        return id;
    }
}
