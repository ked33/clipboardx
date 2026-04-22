using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace ClipboardManager;

/// <summary>
/// 在 <see cref="InlineCollection"/> 中按搜索词分段高亮（大小写不敏感），供资源管理器快搜、剪切板弹窗、文件夹跳转共用。
/// 关键词按空白拆分，并用正则再拆出「连续中文 / 连续英文数字」子串，便于中文与混合输入（如 <c>报告v2</c>）都能命中。
/// </summary>
public static class SearchHighlightInlines
{
    /// <summary>中文区段 + 英文数字等（与 Everything 类文件名常见字符对齐）。</summary>
    private static readonly Regex SegmentTokens = new(
        @"[\p{IsCJKUnifiedIdeographs}\p{IsCJKSymbolsandPunctuation}\p{IsEnclosedCJKLettersandMonths}\p{IsCJKCompatibility}]+|[\w\.\-\+]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

    public static void Append(
        InlineCollection inlines,
        string text,
        string? highlightNeedle,
        Brush normalForeground,
        Brush highlightForeground,
        double fontSize,
        FontWeight baseWeight,
        bool highlightSemiBold = true)
    {
        var tokens = CollectHighlightTokens(highlightNeedle);
        if (tokens.Count == 0)
        {
            inlines.Add(new Run(text)
            {
                Foreground = normalForeground,
                FontSize = fontSize,
                FontWeight = baseWeight,
            });
            return;
        }

        var ranges = new List<(int start, int end)>();
        foreach (var tok in tokens)
        {
            if (tok.Length == 0) continue;
            var pos = 0;
            while (pos < text.Length)
            {
                var idx = Compare.IndexOf(text, tok, pos, CompareOptions.OrdinalIgnoreCase);
                if (idx < 0) break;
                ranges.Add((idx, idx + tok.Length));
                pos = idx + 1;
            }
        }

        if (ranges.Count == 0)
        {
            inlines.Add(new Run(text)
            {
                Foreground = normalForeground,
                FontSize = fontSize,
                FontWeight = baseWeight,
            });
            return;
        }

        ranges.Sort((a, b) => a.start.CompareTo(b.start));
        var merged = new List<(int start, int end)>();
        foreach (var r in ranges)
        {
            if (merged.Count == 0)
            {
                merged.Add(r);
                continue;
            }

            var last = merged[^1];
            if (r.start <= last.end)
                merged[^1] = (last.start, System.Math.Max(last.end, r.end));
            else
                merged.Add(r);
        }

        var cursor = 0;
        foreach (var (s, e) in merged)
        {
            if (s > cursor)
            {
                inlines.Add(new Run(text[cursor..s])
                {
                    Foreground = normalForeground,
                    FontSize = fontSize,
                    FontWeight = baseWeight,
                });
            }

            inlines.Add(new Run(text[s..e])
            {
                Foreground = highlightForeground,
                FontSize = fontSize,
                FontWeight = highlightSemiBold ? FontWeights.SemiBold : baseWeight,
            });
            cursor = e;
        }

        if (cursor < text.Length)
        {
            inlines.Add(new Run(text[cursor..])
            {
                Foreground = normalForeground,
                FontSize = fontSize,
                FontWeight = baseWeight,
            });
        }
    }

    public static IReadOnlyList<string> CollectHighlightTokens(string? highlightNeedle)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(highlightNeedle)) return list;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var t = highlightNeedle.Trim();

        void Add(string seg)
        {
            if (seg.Length == 0 || !seen.Add(seg)) return;
            list.Add(seg);
        }

        foreach (var seg in t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = SegmentTokens.Matches(seg);
            if (m.Count == 0)
                Add(seg);
            else
            {
                foreach (Match x in m)
                    Add(x.Value);
            }
        }

        if (list.Count == 0)
            Add(t);
        return list;
    }
}
