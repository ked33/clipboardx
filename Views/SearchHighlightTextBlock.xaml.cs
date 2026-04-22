using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace ClipboardManager;

/// <summary>
/// 根据 <see cref="DisplayText"/> 与 <see cref="HighlightQuery"/> 渲染带主题色的搜索高亮（与 <see cref="SearchHighlightInlines"/> 一致）。
/// </summary>
public partial class SearchHighlightTextBlock : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty DisplayTextProperty = DependencyProperty.Register(
        nameof(DisplayText),
        typeof(string),
        typeof(SearchHighlightTextBlock),
        new PropertyMetadata("", OnDisplayOrQueryChanged));

    public static readonly DependencyProperty HighlightQueryProperty = DependencyProperty.Register(
        nameof(HighlightQuery),
        typeof(string),
        typeof(SearchHighlightTextBlock),
        new PropertyMetadata(null, OnDisplayOrQueryChanged));

    public static readonly DependencyProperty FontSizeOverrideProperty = DependencyProperty.Register(
        nameof(FontSizeOverride),
        typeof(double),
        typeof(SearchHighlightTextBlock),
        new PropertyMetadata(13.0, OnDisplayOrQueryChanged));

    public static readonly DependencyProperty UseMutedBaseProperty = DependencyProperty.Register(
        nameof(UseMutedBase),
        typeof(bool),
        typeof(SearchHighlightTextBlock),
        new PropertyMetadata(false, OnDisplayOrQueryChanged));

    public static readonly DependencyProperty IsStrikeThroughProperty = DependencyProperty.Register(
        nameof(IsStrikeThrough),
        typeof(bool),
        typeof(SearchHighlightTextBlock),
        new PropertyMetadata(false, OnStrikeChanged));

    private Brush? _normalBrush;
    private Brush? _highlightBrush;

    public SearchHighlightTextBlock()
    {
        InitializeComponent();
        Loaded += (_, _) => Rebuild();
    }

    public string DisplayText
    {
        get => (string)GetValue(DisplayTextProperty);
        set => SetValue(DisplayTextProperty, value ?? "");
    }

    public string? HighlightQuery
    {
        get => (string?)GetValue(HighlightQueryProperty);
        set => SetValue(HighlightQueryProperty, value);
    }

    public double FontSizeOverride
    {
        get => (double)GetValue(FontSizeOverrideProperty);
        set => SetValue(FontSizeOverrideProperty, value);
    }

    public bool UseMutedBase
    {
        get => (bool)GetValue(UseMutedBaseProperty);
        set => SetValue(UseMutedBaseProperty, value);
    }

    public bool IsStrikeThrough
    {
        get => (bool)GetValue(IsStrikeThroughProperty);
        set => SetValue(IsStrikeThroughProperty, value);
    }

    private static void OnDisplayOrQueryChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is SearchHighlightTextBlock c)
            c.Rebuild();
    }

    private static void OnStrikeChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
    {
        if (d is SearchHighlightTextBlock c)
            c.ApplyStrikeThrough();
    }

    private void ApplyStrikeThrough()
    {
        PartText.TextDecorations = IsStrikeThrough ? TextDecorations.Strikethrough : null;
    }

    private void Rebuild()
    {
        TryCacheBrushes();
        var normal = _normalBrush ?? System.Windows.Media.Brushes.Gray;
        var hi = _highlightBrush ?? System.Windows.Media.Brushes.Teal;
        var fs = FontSizeOverride;
        var text = DisplayText ?? "";
        var q = string.IsNullOrWhiteSpace(HighlightQuery) ? null : HighlightQuery.Trim();

        PartText.Inlines.Clear();
        if (string.IsNullOrEmpty(text))
        {
            ApplyStrikeThrough();
            return;
        }

        SearchHighlightInlines.Append(
            PartText.Inlines,
            text,
            q,
            normal,
            hi,
            fs,
            FontWeights.Normal);
        ApplyStrikeThrough();
    }

    private void TryCacheBrushes()
    {
        if (_normalBrush != null && _highlightBrush != null) return;
        try
        {
            _normalBrush ??= (Brush)(UseMutedBase
                ? FindResource("MutedText")
                : FindResource("PrimaryText"));
        }
        catch
        {
            _normalBrush ??= System.Windows.Media.Brushes.Gainsboro;
        }

        try
        {
            _highlightBrush ??= (Brush)FindResource("AccentBg");
        }
        catch
        {
            _highlightBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x13, 0x94, 0x93));
        }
    }
}
