using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ClipboardManager;

public enum EntryType { Text, Image, Files }

public class ClipboardEntry : INotifyPropertyChanged
{
    public EntryType Type { get; set; }
    public string? TextContent { get; set; }
    public byte[]? ImageData { get; set; }
    public string[]? FilePaths { get; set; }

    private string? _imageMd5Hex;
    /// <summary>PNG 图像字节的 MD5（小写十六进制），惰性计算；用于历史项去重。</summary>
    public string? ImageContentMd5Hex
    {
        get
        {
            if (Type != EntryType.Image || ImageData is not { Length: > 0 }) return null;
            if (_imageMd5Hex != null) return _imageMd5Hex;
            _imageMd5Hex = ComputeImageBytesMd5Hex(ImageData);
            return _imageMd5Hex;
        }
    }

    public static string ComputeImageBytesMd5Hex(byte[] data)
    {
        if (data == null || data.Length == 0) return "";
        using var md5 = MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(data)).ToLowerInvariant();
    }
    public DateTime CopiedAt { get; set; } = DateTime.Now;
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }

    /// <summary>Windows OCR 识别出的图片内文字；空表示无文字或尚未识别。</summary>
    public string? OcrText { get; set; }

    private bool _isOcrPending;
    /// <summary>后台 OCR 队列处理中。</summary>
    public bool IsOcrPending
    {
        get => _isOcrPending;
        set
        {
            if (_isOcrPending == value) return;
            _isOcrPending = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOcrPending)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubInfo)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSubInfo)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubInfoVisibility)));
        }
    }

    public void RaiseOcrDisplayPropertiesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchableText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubInfo)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSubInfo)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubInfoVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageMetaLine)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasImageMetaLine)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageMetaLineVisibility)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OcrPreviewBody)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasOcrPreviewBody)));
    }

    public bool IsQuickPaste { get; set; }
    public string? ShortcutPhrase { get; set; }

    /// <summary>SQLite 主键；非持久化条目（如尚未入库或快捷短语）为 null。</summary>
    public long? PersistedId { get; set; }

    /// <summary>粘贴或置顶时更新复制时间并通知列表「时间」列刷新。</summary>
    public void TouchCopiedTime()
    {
        CopiedAt = DateTime.Now;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeAgo)));
    }

    /// <summary>就地修改 <see cref="TextContent"/> 后通知列表预览与检索绑定刷新。</summary>
    public void RaiseTextDisplayPropertiesChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SearchableText)));
    }

    private bool _isPendingDelete;
    /// <summary>Del 第一次按下时为 true，表示待二次确认删除（删除线提示）。</summary>
    public bool IsPendingDelete
    {
        get => _isPendingDelete;
        set
        {
            if (_isPendingDelete == value) return;
            _isPendingDelete = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPendingDelete)));
        }
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    public static int PreviewMaxLines { get; set; } = 2;

    public string IndexLabel => DisplayIndex >= 1 && DisplayIndex <= 9 ? DisplayIndex.ToString() : "";

    private int _batchOrder;
    /// <summary>批量粘贴队列中的顺序（1 起）；0 表示不在队列。</summary>
    public int BatchOrder
    {
        get => _batchOrder;
        set
        {
            if (_batchOrder == value) return;
            _batchOrder = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BatchOrder)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasBatchOrder)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BatchOrderLabel)));
        }
    }

    public bool HasBatchOrder => _batchOrder > 0;
    public string BatchOrderLabel => _batchOrder > 0 ? _batchOrder.ToString() : "";

    private BitmapSource? _thumbnail;

    public BitmapSource? Thumbnail
    {
        get
        {
            if (_thumbnail == null)
            {
                if (ImageData != null) _thumbnail = CreateThumbnail();
                else if (IsImageFile) _thumbnail = CreateFileThumbnail();
            }
            return _thumbnail;
        }
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico"
    };

    public bool IsImageFile => Type == EntryType.Files
        && FilePaths is { Length: >= 1 }
        && ImageExtensions.Contains(Path.GetExtension(FilePaths[0]));

    /// <summary>Files 条目中属于图片的文件数量（按扩展名判断）。</summary>
    public int ImageFileCount
    {
        get
        {
            if (Type != EntryType.Files || FilePaths is null) return 0;
            int n = 0;
            foreach (var p in FilePaths)
            {
                if (ImageExtensions.Contains(Path.GetExtension(p))) n++;
            }
            return n;
        }
    }

    /// <summary>Files 条目包含多张图片（用于预览气泡多图导航）。</summary>
    public bool IsMultiImageFiles => Type == EntryType.Files && ImageFileCount > 1;

    /// <summary>返回 Files 条目中所有图片文件路径（仅图片扩展名）。</summary>
    public string[] GetImageFilePaths()
    {
        if (Type != EntryType.Files || FilePaths is null) return Array.Empty<string>();
        var list = new List<string>(FilePaths.Length);
        foreach (var p in FilePaths)
        {
            if (ImageExtensions.Contains(Path.GetExtension(p))) list.Add(p);
        }
        return list.ToArray();
    }

    public bool HasThumbnail => Type == EntryType.Image || IsImageFile;
    public bool HasIcon => !HasThumbnail;

    public string TypeIcon => Type switch
    {
        EntryType.Text => IsQuickPaste ? "⚡" : "📝",
        EntryType.Image => "🖼️",
        EntryType.Files => IsImageFile ? "🖼️" : "📁",
        _ => ""
    };

    public string Preview => Type switch
    {
        EntryType.Text => TruncateText(TextContent, PreviewMaxLines, 200),
        EntryType.Image => BuildImagePreview(),
        EntryType.Files => FormatFilePaths(),
        _ => ""
    };

    private string BuildImagePreview()
    {
        if (!string.IsNullOrWhiteSpace(OcrText))
            return TruncateText(NormalizeOcrDisplayText(OcrText), PreviewMaxLines, 160);
        if (IsOcrPending)
            return "识别文字中…";
        return $"{ImageWidth}×{ImageHeight} 图片";
    }

    /// <summary>列表副行：尺寸、识别状态等元信息。</summary>
    public string? ImageMetaLine
    {
        get
        {
            if (Type != EntryType.Image) return null;
            var dim = $"{ImageWidth}×{ImageHeight}";
            if (IsOcrPending) return $"{dim} · 识别中";
            if (!string.IsNullOrWhiteSpace(OcrText)) return $"{dim} · 图片";
            return null;
        }
    }

    public bool HasImageMetaLine => ImageMetaLine != null;

    public Visibility ImageMetaLineVisibility => HasImageMetaLine ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>预览气泡中展示的 OCR 全文（适度截断）。</summary>
    public string? OcrPreviewBody
    {
        get
        {
            if (Type != EntryType.Image || string.IsNullOrWhiteSpace(OcrText)) return null;
            var t = NormalizeOcrDisplayText(OcrText);
            return t.Length > 4000 ? t[..4000] + "…" : t;
        }
    }

    public bool HasOcrPreviewBody => !string.IsNullOrWhiteSpace(OcrPreviewBody);

    private static string NormalizeOcrDisplayText(string text) =>
        OcrTextPostProcessor.Normalize(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim());

    public string? SubInfo
    {
        get
        {
            if (ShortcutPhrase != null) return $"⚡ {ShortcutPhrase}";
            if (Type == EntryType.Files && FilePaths is { Length: > 1 }) return $"{FilePaths.Length} 个文件";
            if (Type == EntryType.Image) return ImageMetaLine;
            return null;
        }
    }

    public bool HasSubInfo => SubInfo != null;

    public string SearchableText
    {
        get
        {
            var baseText = Type switch
            {
                EntryType.Text => TextContent ?? "",
                EntryType.Files => string.Join(" ", FilePaths?.Select(Path.GetFileName) ?? []),
                EntryType.Image => BuildImageSearchableText(),
                _ => ""
            };
            return ShortcutPhrase != null ? $"{ShortcutPhrase} {baseText}" : baseText;
        }
    }

    private string BuildImageSearchableText()
    {
        var dim = $"image 图片 {ImageWidth}x{ImageHeight}";
        return string.IsNullOrWhiteSpace(OcrText) ? dim : $"{dim} {OcrText}";
    }

    public bool MatchesSearch(string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return SearchableText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public string TimeAgo
    {
        get
        {
            var span = DateTime.Now - CopiedAt;
            if (span.TotalSeconds < 60) return "刚刚";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}分钟前";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}小时前";
            return CopiedAt.ToString("MM-dd HH:mm");
        }
    }

    public Visibility IconVisibility => HasIcon ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ThumbnailVisibility => HasThumbnail ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SubInfoVisibility => HasSubInfo ? Visibility.Visible : Visibility.Collapsed;

    private static string TruncateText(string? text, int maxLines, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var lines = text.Split('\n');
        var taken = lines.Take(maxLines).Select(l => l.TrimEnd('\r'));
        var result = string.Join("\n", taken);
        if (result.Length > maxChars)
            result = result[..maxChars] + "…";
        else if (lines.Length > maxLines)
            result += " …";
        return result;
    }

    private string FormatFilePaths()
    {
        if (FilePaths == null || FilePaths.Length == 0) return "";
        var names = FilePaths.Select(Path.GetFileName).Take(3);
        var result = string.Join(", ", names);
        if (FilePaths.Length > 3) result += $" (+{FilePaths.Length - 3})";
        return result;
    }

    private BitmapSource? CreateThumbnail()
    {
        try
        {
            using var ms = new MemoryStream(ImageData!);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.StreamSource = ms;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 64;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private BitmapSource? CreateFileThumbnail()
    {
        try
        {
            var path = FilePaths![0];
            if (!File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path);
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 64;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    public static byte[]? EncodeToPng(BitmapSource image)
    {
        try
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }
}
