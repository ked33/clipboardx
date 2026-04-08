using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Text;
using Svg;

namespace ClipboardManager;

/// <summary>
/// 由用户提供的 Iconfont 剪贴板 SVG（viewBox 0 0 1024 1024）光栅化为托盘/窗口图标。
/// 若改路径或配色，请同步更新 <c>tools/GenAppIcon</c> 并重新生成 <c>assets/clipboard.ico</c>（供 exe / 快捷方式嵌入图标）。
/// </summary>
internal static class TrayIconSvg
{
    /// <summary>主色（青绿 #139493）。</summary>
    public const string MainBlueHex = "#139493";

    /// <summary>中间横条：同系浅色，小图可辨。</summary>
    public const string BarBlueHex = "#B5E8E7";

    /// <summary>FIFO：主蓝 + 浅条。</summary>
    public const string FifoMainHex = "#2563EB";

    public const string FifoBarHex = "#BFDBFE";

    /// <summary>LIFO：金黄主色 + 浅条。</summary>
    public const string LifoMainHex = "#CA8A04";

    public const string LifoBarHex = "#FEF08A";

    private const string PathClipboard =
        "M880 192H768c-8.8 0-16-7.2-16-16V64c0-35.4-28.7-64-64-64H144c-35.3 0-64 28.6-64 64v704c0 35.3 28.7 64 64 64h112c8.8 0 16 7.2 16 16v112c0 35.3 28.7 64 64 64h544c35.3 0 64-28.7 64-64V256c0-35.4-28.7-64-64-64z m0 752c0 8.8-7.2 16-16 16H352c-8.8 0-16-7.2-16-16V272c0-8.8 7.2-16 16-16h512c8.8 0 16 7.2 16 16v672z";

    private const string PathBar =
        "M704 352H512c-17.7 0-32-14.3-32-32s14.3-32 32-32h192c17.7 0 32 14.3 32 32s-14.3 32-32 32z";

    /// <summary>与托盘主色一致，供顶栏 Tag、列表角标等复用。</summary>
    public static string GetModeMainHex(BatchPasteQueueMode mode) => mode switch
    {
        BatchPasteQueueMode.Fifo => FifoMainHex,
        BatchPasteQueueMode.Lifo => LifoMainHex,
        _ => MainBlueHex,
    };

    public static Icon CreateIcon(int size = 32, BatchPasteQueueMode batchMode = BatchPasteQueueMode.Off)
    {
        string mainHex;
        string barHex;
        char? letter;
        switch (batchMode)
        {
            case BatchPasteQueueMode.Fifo:
                mainHex = FifoMainHex;
                barHex = FifoBarHex;
                letter = 'F';
                break;
            case BatchPasteQueueMode.Lifo:
                mainHex = LifoMainHex;
                barHex = LifoBarHex;
                letter = 'L';
                break;
            default:
                mainHex = MainBlueHex;
                barHex = BarBlueHex;
                letter = null;
                break;
        }

        var svg = $"""
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024">
  <path fill="{mainHex}" d="{PathClipboard}"/>
  <path fill="{barHex}" d="{PathBar}"/>
</svg>
""";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(svg));
        var doc = SvgDocument.Open<SvgDocument>(ms);
        using var rendered = doc.Draw(size, size);

        if (letter == null)
        {
            using var tmp = Icon.FromHandle(rendered.GetHicon());
            return (Icon)tmp.Clone();
        }

        using var bmp = new Bitmap(rendered);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            float em = Math.Max(7.5f, size * 0.50f);
            using var font = new Font("Segoe UI", em, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fill = new SolidBrush(ColorTranslator.FromHtml(mainHex));
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            float ox = size * 0.083f;
            float oy = size * 0.035f - size * 0.06f + size * 0.018f + size * 0.084f; // 再略右下微调
            var layout = new RectangleF(ox, oy, size, size);
            g.DrawString(letter.Value.ToString(), font, fill, layout, sf);
        }

        using (var tmp = Icon.FromHandle(bmp.GetHicon()))
            return (Icon)tmp.Clone();
    }
}
