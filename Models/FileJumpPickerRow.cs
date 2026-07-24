using System.ComponentModel;
using System.Windows;

namespace ClipboardManager;

/// <summary>跳转列表中的一行（动态路径 + 收藏），支持检索与快捷标号。</summary>
public sealed class FileJumpPickerRow : INotifyPropertyChanged
{
    public FileJumpPickerRow(string sourceLabel, string path, bool isFavorite, string? phrase = null)
    {
        SourceLabel = sourceLabel;
        Path = path;
        IsFavorite = isFavorite;
        Phrase = phrase ?? "";
    }

    public string SourceLabel { get; }
    public string Path { get; }
    public bool IsFavorite { get; }
    public string Phrase { get; }

    private int _displayIndex;
    public int DisplayIndex
    {
        get => _displayIndex;
        set
        {
            if (_displayIndex == value) return;
            _displayIndex = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayIndex)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IndexLabel)));
        }
    }

    public string IndexLabel => DisplayIndex is >= 1 and <= 9 ? DisplayIndex.ToString() : "";

    public string TypeIcon => IsFavorite ? "⭐" : "📁";

    public string PreviewLine => IsFavorite && !string.IsNullOrEmpty(Phrase)
        ? $"{Phrase}  ·  {SourceLabel}"
        : SourceLabel;

    public string PathLine => Path;

    public string PathLineTruncated => TruncatePathMiddle(Path);

    public string? SubInfo => IsFavorite && !string.IsNullOrEmpty(Phrase) ? $"⚡ {Phrase}" : null;

    public Visibility SubInfoVisibility => string.IsNullOrEmpty(SubInfo) ? Visibility.Collapsed : Visibility.Visible;

    public string SearchablePrimary =>
        $"{Phrase} {SourceLabel} {Path}";

    /// <summary>
    /// 路径过长时中间省略，并尽量保证最里层目录名完整显示。
    /// 形如：C:\Users\Docs\…\InnermostDir
    /// </summary>
    private static string TruncatePathMiddle(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        const int maxLen = 50;
        if (path.Length <= maxLen) return path;

        const string ellipsis = "…";

        // 去掉末尾分隔符再分析段。注意：本类有实例属性 Path，勿写 Path.GetFileName。
        var core = path.TrimEnd('\\', '/');
        if (core.Length == 0)
            return path[..(maxLen - 1)] + ellipsis;
        if (core.Length <= maxLen)
            return core;

        var lastSep = -1;
        for (var i = core.Length - 1; i >= 0; i--)
        {
            if (core[i] is '\\' or '/')
            {
                lastSep = i;
                break;
            }
        }

        // 最里层目录/文件名必须优先完整保留
        var leaf = lastSep < 0 ? core : core[(lastSep + 1)..];

        // 叶子本身超过预算：只能保留叶子末尾
        if (ellipsis.Length + leaf.Length >= maxLen)
        {
            var take = maxLen - ellipsis.Length;
            return take > 0 ? ellipsis + leaf[^take..] : ellipsis;
        }

        // 无分隔符：退化为首尾截断
        if (lastSep < 0)
        {
            var keep = maxLen - ellipsis.Length;
            var headChars = keep / 2;
            var tailChars = keep - headChars;
            return core[..headChars] + ellipsis + core[^tailChars..];
        }

        // 尾部从「\leaf」起，可再向左纳入完整父级段
        var tailStart = lastSep; // core[tailStart..] == "\leaf"
        const int minHead = 3; // 尽量保留 "C:\" 一类前缀
        var maxTailLen = maxLen - ellipsis.Length - minHead;

        var searchFrom = lastSep - 1;
        while (searchFrom >= 0)
        {
            var prevSep = -1;
            for (var i = searchFrom; i >= 0; i--)
            {
                if (core[i] is '\\' or '/')
                {
                    prevSep = i;
                    break;
                }
            }

            // 纳入上一段：从该段前的分隔符起（或从路径开头的段名起）
            var candidateStart = prevSep >= 0 ? prevSep : 0;
            // candidateStart==0 时 tail 几乎是全路径，与 head 省略组合无意义，停止
            if (candidateStart == 0)
                break;

            var candidateLen = core.Length - candidateStart;
            if (candidateLen > maxTailLen)
                break;

            tailStart = candidateStart;
            searchFrom = prevSep - 1;
        }

        var tail = core[tailStart..];
        // 此时 tail 至少含完整 leaf（通常带前导分隔符）
        var headBudget = maxLen - ellipsis.Length - tail.Length;
        if (headBudget <= 0)
            return ellipsis + tail;

        // 前缀不得与 tail 重叠
        if (headBudget > tailStart)
            headBudget = tailStart;
        if (headBudget <= 0)
            return ellipsis + tail;

        var headEnd = headBudget;
        // 前缀落在完整段边界（含分隔符），避免 "C:\Use…" 半截目录名
        for (var i = headEnd - 1; i >= 0; i--)
        {
            if (core[i] is '\\' or '/')
            {
                if (i + 1 >= 2)
                    headEnd = i + 1;
                break;
            }
        }

        return core[..headEnd] + ellipsis + tail;
    }

    public bool MatchesSearch(string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return SearchablePrimary.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
