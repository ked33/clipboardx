using System.Text;
using System.Text.RegularExpressions;
using Windows.Media.Ocr;

namespace ClipboardManager;

/// <summary>修正 Windows OCR 在 CJK 文本中误插的空格（引擎按单字分词后用空格拼接）。</summary>
internal static class OcrTextPostProcessor
{
    private static readonly Regex InterCjkSpaceRegex = new(
        @"(?<=[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF\u3040-\u309F\u30A0-\u30FF\uAC00-\uD7AF\u3130-\u318F])" +
        @"[ \t\u3000]+" +
        @"(?=[\u4E00-\u9FFF\u3400-\u4DBF\uF900-\uFAFF\u3040-\u309F\u30A0-\u30FF\uAC00-\uD7AF\u3130-\u318F])",
        RegexOptions.Compiled);

    public static string? FormatResult(OcrResult? result)
    {
        if (result == null) return null;

        if (result.Lines is { Count: > 0 })
        {
            var sb = new StringBuilder();
            foreach (var line in result.Lines)
            {
                if (sb.Length > 0)
                    sb.AppendLine();

                var words = line.Words;
                if (words == null || words.Count == 0) continue;

                for (var i = 0; i < words.Count; i++)
                {
                    var w = words[i].Text;
                    if (string.IsNullOrEmpty(w)) continue;

                    if (i > 0 && sb.Length > 0 && sb[^1] != '\n')
                    {
                        var prev = words[i - 1].Text ?? "";
                        if (ShouldInsertSpaceBetweenWords(prev, w))
                            sb.Append(' ');
                    }

                    sb.Append(w);
                }
            }

            var built = Normalize(sb.ToString());
            if (!string.IsNullOrWhiteSpace(built))
                return built;
        }

        var fallback = result.Text;
        return string.IsNullOrWhiteSpace(fallback) ? null : Normalize(fallback);
    }

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text.Trim();
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        text = InterCjkSpaceRegex.Replace(text, "");
        return text.Trim();
    }

    private static bool ShouldInsertSpaceBetweenWords(string previousWord, string nextWord)
    {
        if (string.IsNullOrEmpty(previousWord) || string.IsNullOrEmpty(nextWord)) return false;

        var a = previousWord[^1];
        var b = nextWord[0];
        var aCjk = IsCjkChar(a);
        var bCjk = IsCjkChar(b);
        if (aCjk && bCjk) return false;

        var aLatin = UsesLatinWordSpacing(a);
        var bLatin = UsesLatinWordSpacing(b);
        if (aLatin && bLatin) return true;

        if (aCjk != bCjk) return true;

        return false;
    }

    private static bool UsesLatinWordSpacing(char c) =>
        c <= 0x7F ? char.IsLetterOrDigit(c) : char.IsLetter(c) && !IsCjkChar(c);

    private static bool IsCjkChar(char c) =>
        c is >= '\u4E00' and <= '\u9FFF'
        or >= '\u3400' and <= '\u4DBF'
        or >= '\uF900' and <= '\uFAFF'
        or >= '\u3040' and <= '\u309F'
        or >= '\u30A0' and <= '\u30FF'
        or >= '\uAC00' and <= '\uD7AF'
        or >= '\u3130' and <= '\u318F';
}
