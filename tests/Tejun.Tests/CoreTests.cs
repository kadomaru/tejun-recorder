using System.Text;
using SkiaSharp;
using Tejun.Core;
using Tejun.Core.Export;
using Xunit;

namespace Tejun.Tests;

public class DescriberTests
{
    [Theory]
    [InlineData(ClickKind.Left, "保存", "ボタン", "メモ帳", "「保存」ボタンをクリックします")]
    [InlineData(ClickKind.Right, "受信トレイ", "", "メール", "「受信トレイ」を右クリックします")]
    [InlineData(ClickKind.DoubleLeft, "", "", "エクスプローラー", "「エクスプローラー」の画面で、印の場所をダブルクリックします")]
    [InlineData(ClickKind.Left, "", "", "", "印の場所をクリックします")]
    [InlineData(ClickKind.Left, "閉じるボタン", "ボタン", "x", "「閉じるボタン」をクリックします")]
    public void Describes(ClickKind kind, string element, string type, string window, string expected) =>
        Assert.Equal(expected, Describer.Describe(kind, element, type, window));

    [Fact]
    public void CleansLongAndMultilineNames()
    {
        Assert.Equal("a b", Describer.Clean(" a \r\n b ", 10));
        Assert.Equal("12345…", Describer.Clean("1234567", 5));
    }
}

public class TextTests
{
    [Fact]
    public void WrapsWithinWidthAndKeepsAllText()
    {
        using var text = new TextPainter(12);
        var source = "これは長い日本語の文章です。English words should not be split in the middle、句読点は行頭に来ません。";
        var lines = text.Wrap(source, 120);
        Assert.True(lines.Count > 3);
        Assert.All(lines, l => Assert.True(text.Measure(l) <= 120.5f, l));
        Assert.Equal(source.Replace(" ", ""), string.Concat(lines).Replace(" ", ""));
        Assert.DoesNotContain(lines, l => l.StartsWith('、') || l.StartsWith('。'));
    }

    [Fact]
    public void JapaneseFontIsAvailable()
    {
        using var text = new TextPainter(12);
        Assert.True(text.Measure("あ") > 6);
    }
}

public class ExportTests
{
    [Fact]
    public void MarkerIsDrawnAtClickPosition()
    {
        var dir = Samples.TempDir("marker");
        var step = Samples.Step(dir, 800, 600, 200, 150, ClickKind.Left, "x");
        using var image = StepImage.Render(step);
        using var bitmap = SKBitmap.FromImage(image);
        Assert.Equal(800, bitmap.Width);
        Assert.Equal(600, bitmap.Height);
        var r = (int)Marker.Radius(step);
        var onRing = bitmap.GetPixel(200 + r, 150);
        Assert.True(onRing.Red > 230 && onRing.Green < 110 && onRing.Blue < 110, onRing.ToString());
        // 印から離れた場所は元の画像のまま
        Assert.Equal(new SKColor(0xEC, 0xEF, 0xF4), bitmap.GetPixel(700, 50));
    }

    [Fact]
    public void NoMarkerForPlainCapture()
    {
        var dir = Samples.TempDir("nomarker");
        var step = Samples.Step(dir, 800, 600, 200, 150, ClickKind.None, "x");
        using var image = StepImage.Render(step);
        using var bitmap = SKBitmap.FromImage(image);
        using var original = SKBitmap.Decode(step.ImagePath);
        for (var x = 150; x < 250; x += 7)
            Assert.Equal(original.GetPixel(x, 150), bitmap.GetPixel(x, 150));
    }

    [Fact]
    public void PngExportWritesOneFilePerStep()
    {
        var dir = Samples.TempDir("png");
        var recording = Samples.Recording(dir);
        var files = PngExporter.Export(recording, Path.Combine(dir, "out"));
        Assert.Equal(5, files.Count);
        Assert.EndsWith("手順01.png", files[0]);
        using var first = SKBitmap.Decode(files[0]);
        Assert.Equal(1920, first.Width);
        Assert.True(first.Height > 1080); // 説明の帯の分だけ高い
        Artifacts.Keep(files[2], "png-step3.png");
        Artifacts.Keep(files[1], "png-step2.png");
    }

    [Fact]
    public void ExportWithoutDescriptions()
    {
        var dir = Samples.TempDir("nocaption");
        var recording = Samples.Recording(dir);

        // PNG は帯が付かず、元の画面と同じ大きさになる
        var files = PngExporter.Export(recording, Path.Combine(dir, "out"), caption: false);
        Assert.Equal(5, files.Count);
        using var first = SKBitmap.Decode(files[0]);
        Assert.Equal(1920, first.Width);
        Assert.Equal(1080, first.Height);
        Artifacts.Keep(files[2], "png-step3-nocaption.png");

        // HTML は番号と画像だけ
        var html = Path.Combine(dir, "out.html");
        HtmlExporter.Export(recording, html, caption: false);
        var text = File.ReadAllText(html, Encoding.UTF8);
        Assert.Equal(5, text.Split("data:image/png;base64,").Length - 1);
        Assert.Contains("<span class=\"num\">1</span></div>", text);
        Assert.DoesNotContain("&lt;OK&gt;", text);
        Artifacts.Keep(html, "sample-nocaption.html");

        // PDF は説明の分だけ小さくなる
        var with = Path.Combine(dir, "with.pdf");
        var without = Path.Combine(dir, "without.pdf");
        PdfExporter.Export(recording, with);
        PdfExporter.Export(recording, without, caption: false);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(File.ReadAllBytes(without), 0, 5));
        Assert.True(new FileInfo(without).Length < new FileInfo(with).Length);
        Artifacts.Keep(without, "sample-nocaption.pdf");
    }

    [Fact]
    public void HtmlExportIsSelfContainedAndEscaped()
    {
        var dir = Samples.TempDir("html");
        var recording = Samples.Recording(dir);
        var path = Path.Combine(dir, "out.html");
        HtmlExporter.Export(recording, path);
        var html = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("<title>メールの署名を設定する手順</title>", html);
        Assert.Equal(5, html.Split("data:image/png;base64,").Length - 1);
        Assert.Contains("&lt;OK&gt; &amp; &quot;保存&quot;", html);
        Assert.DoesNotContain("src=\"http", html);
        Artifacts.Keep(path, "sample.html");
    }

    [Fact]
    public void PdfExportHasPagesAndSearchableTitle()
    {
        var dir = Samples.TempDir("pdf");
        var recording = Samples.Recording(dir);
        var path = Path.Combine(dir, "out.pdf");
        PdfExporter.Export(recording, path);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        var text = Encoding.Latin1.GetString(bytes);
        var pages = System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page\b").Count;
        Assert.InRange(pages, 2, 5);
        Assert.Contains("/Font", text);
        // 書体を丸ごと埋め込むと 10MB を超える。使う文字だけなら小さく収まる
        Assert.True(bytes.Length < 3_000_000, $"PDF が大きすぎます: {bytes.Length:N0} バイト");
        Artifacts.Keep(path, "sample.pdf");
    }

    [Fact]
    public void SafeFileNames()
    {
        Assert.Equal("a_b_c", FileNames.Safe("a/b:c"));
        Assert.Equal("手順", FileNames.Safe("  "));
    }
}

public class FontSubsetTests
{
    [Fact]
    public void SubsetKeepsGlyphsAndWidthsButIsSmall()
    {
        // この環境にある TrueType 形式の書体で確かめる（無ければ確かめようがないので何もしない）
        var manager = SKFontManager.Default;
        var face = manager.FontFamilies
            .Select(f => manager.MatchFamily(f))
            .FirstOrDefault(f => f != null && f.ContainsGlyph('A') && f.GetTableTags().Contains(0x676C7966u /* glyf */));
        if (face == null) return;

        var text = "ABC abc 123";
        var cps = text.Select(c => (int)c).ToArray();
        var subset = FontSubsetter.Subset(face, cps);
        Assert.NotNull(subset);
        using var original = face.OpenStream(out _);
        using var reduced = subset!.OpenStream(out _);
        Assert.True(reduced.Length < original.Length, $"{reduced.Length} / {original.Length}");
        using var a = new SKFont(face, 20);
        using var b = new SKFont(subset, 20);
        Assert.Equal(a.MeasureText(text), b.MeasureText(text), 2);
        Assert.Equal(face.GetGlyph('A'), subset.GetGlyph('A'));
        Assert.False(subset.ContainsGlyph('Z'));

        // 絞り込んだ書体で実際に字が描ける
        using var bitmap = new SKBitmap(200, 40);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawText(text, 4, 28, SKTextAlign.Left, b, paint);
        var dark = 0;
        for (var y = 0; y < 40; y++) for (var x = 0; x < 200; x++) if (bitmap.GetPixel(x, y).Red < 128) dark++;
        Assert.True(dark > 100, dark.ToString());
    }
}

/// <summary>目で確かめたい出力を 1 か所に集める（TEJUN_ARTIFACTS を指定したときだけ）。</summary>
public static class Artifacts
{
    public static string? Dir => Environment.GetEnvironmentVariable("TEJUN_ARTIFACTS");

    public static void Keep(string path, string name)
    {
        if (string.IsNullOrEmpty(Dir)) return;
        Directory.CreateDirectory(Dir);
        File.Copy(path, Path.Combine(Dir, name), true);
    }
}
