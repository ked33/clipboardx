using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using Svg;

namespace GenAppIcon;

/// <summary>由 TrayIconSvg 同源 SVG 生成多分辨率 PNG 型 .ico，供 ApplicationIcon 使用。</summary>
internal static class Program
{
    private const string MainBlueHex = "#139493";
    private const string BarBlueHex = "#B5E8E7";

    private const string PathClipboard =
        "M880 192H768c-8.8 0-16-7.2-16-16V64c0-35.4-28.7-64-64-64H144c-35.3 0-64 28.6-64 64v704c0 35.3 28.7 64 64 64h112c8.8 0 16 7.2 16 16v112c0 35.3 28.7 64 64 64h544c35.3 0 64-28.7 64-64V256c0-35.4-28.7-64-64-64z m0 752c0 8.8-7.2 16-16 16H352c-8.8 0-16-7.2-16-16V272c0-8.8 7.2-16 16-16h512c8.8 0 16 7.2 16 16v672z";

    private const string PathBar =
        "M704 352H512c-17.7 0-32-14.3-32-32s14.3-32 32-32h192c17.7 0 32 14.3 32 32s-14.3 32-32 32z";

    private static int Main(string[] args)
    {
        // 默认：仓库根目录 assets/clipboard.ico（从 tools/GenAppIcon/bin/.../net8.0 向上 5 层）
        var outPath = args.Length > 0
            ? args[0]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets",
                "clipboard.ico"));

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var pngList = new List<(int size, byte[] png)>(sizes.Length);
        foreach (var s in sizes)
            pngList.Add((s, RenderPng(s)));

        WriteIcoWithPngEntries(outPath, pngList);
        Console.WriteLine($"Wrote {outPath}");
        return 0;
    }

    private static byte[] RenderPng(int size)
    {
        var svg = $"""
<?xml version="1.0" encoding="UTF-8"?>
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024">
  <path fill="{MainBlueHex}" d="{PathClipboard}"/>
  <path fill="{BarBlueHex}" d="{PathBar}"/>
</svg>
""";
        using var msIn = new MemoryStream(Encoding.UTF8.GetBytes(svg));
        var doc = SvgDocument.Open<SvgDocument>(msIn);
        using var bmp = doc.Draw(size, size);
        using var msOut = new MemoryStream();
        bmp.Save(msOut, ImageFormat.Png);
        return msOut.ToArray();
    }

    /// <summary>Windows Vista+ 支持的 PNG 内嵌型 ICO。</summary>
    private static void WriteIcoWithPngEntries(string path, IReadOnlyList<(int size, byte[] png)> images)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        bw.Write((ushort)0); // reserved
        bw.Write((ushort)1); // type: icon
        bw.Write((ushort)images.Count);

        var headerBytes = 6 + 16 * images.Count;
        var offset = headerBytes;

        foreach (var (dim, png) in images)
        {
            bw.Write((byte)(dim >= 256 ? 0 : dim));
            bw.Write((byte)(dim >= 256 ? 0 : dim));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)0); // PNG: planes 0
            bw.Write((ushort)0); // PNG: bpp 0
            bw.Write(png.Length);
            bw.Write(offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
            bw.Write(png);
    }
}
