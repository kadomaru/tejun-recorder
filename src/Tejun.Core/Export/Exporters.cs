using System.Net;
using System.Text;
using SkiaSharp;

namespace Tejun.Core.Export;

public static class FileNames
{
    /// <summary>ファイル名に使えない文字を取り除く。</summary>
    public static string Safe(string? name, string fallback = "手順")
    {
        var s = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        var bad = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        s = new string(s.Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (s.Length > 80) s = s[..80];
        return s.Length == 0 ? fallback : s;
    }
}

public static class PngExporter
{
    /// <summary>
    /// 手順ごとに 1 枚の PNG をフォルダーへ書き出す。戻り値は書いたファイルの一覧。
    /// <paramref name="caption"/> を外すと、番号と説明の帯を付けず、印を描いた画面だけにする。
    /// </summary>
    public static List<string> Export(Recording recording, string folder, bool caption = true, IProgress<double>? progress = null)
    {
        Directory.CreateDirectory(folder);
        var files = new List<string>();
        var digits = Math.Max(2, recording.Steps.Count.ToString().Length);
        for (var i = 0; i < recording.Steps.Count; i++)
        {
            var path = Path.Combine(folder, $"手順{(i + 1).ToString().PadLeft(digits, '0')}.png");
            File.WriteAllBytes(path, StepImage.RenderPng(recording.Steps[i], i + 1, caption));
            files.Add(path);
            progress?.Report((i + 1.0) / recording.Steps.Count);
        }
        return files;
    }
}

public static class HtmlExporter
{
    /// <summary>
    /// 画像を埋め込んだ、1 ファイルで完結する HTML を書き出す。
    /// <paramref name="caption"/> を外すと、各手順は番号と画像だけになる。
    /// </summary>
    public static void Export(Recording recording, string path, bool caption = true, IProgress<double>? progress = null)
    {
        var title = string.IsNullOrWhiteSpace(recording.Title) ? "手順書" : recording.Title.Trim();
        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html lang=\"ja\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<meta name=\"generator\" content=\"手順レコーダー\">\n");
        sb.Append($"<title>{E(title)}</title>\n<style>\n{Css}</style>\n</head>\n<body>\n");
        sb.Append($"<header>\n<h1>{E(title)}</h1>\n<p class=\"meta\">{recording.CreatedAt:yyyy年M月d日} ・ 全 {recording.Steps.Count} 手順</p>\n</header>\n<ol class=\"steps\">\n");
        for (var i = 0; i < recording.Steps.Count; i++)
        {
            var step = recording.Steps[i];
            var png = StepImage.RenderPng(step);
            var description = caption ? $"<p>{E(step.Description).Replace("\n", "<br>")}</p>" : "";
            sb.Append($"<li class=\"step\" id=\"step-{i + 1}\">\n<div class=\"head\"><span class=\"num\">{i + 1}</span>{description}</div>\n");
            sb.Append($"<a class=\"shot\" href=\"#zoom-{i + 1}\"><img loading=\"lazy\" width=\"{step.ImageWidth}\" height=\"{step.ImageHeight}\" alt=\"手順 {i + 1} の画面\" src=\"data:image/png;base64,");
            sb.Append(Convert.ToBase64String(png));
            sb.Append($"\"></a>\n<a class=\"zoom\" id=\"zoom-{i + 1}\" href=\"#step-{i + 1}\" aria-label=\"拡大表示を閉じる\"></a>\n</li>\n");
            progress?.Report((i + 1.0) / recording.Steps.Count);
        }
        sb.Append("</ol>\n<script>\n" + Script + "</script>\n</body>\n</html>\n");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    static string E(string s) => WebUtility.HtmlEncode(s);

    const string Css = """
        :root { color-scheme: light dark; --bg: #f5f5f7; --card: #fff; --text: #1d1d1f; --sub: #6e6e73; --line: #d2d2d7; --accent: #ff3b30; }
        @media (prefers-color-scheme: dark) { :root { --bg: #1c1c1e; --card: #2c2c2e; --text: #f5f5f7; --sub: #a1a1a6; --line: #48484a; } }
        * { box-sizing: border-box; }
        body { margin: 0; background: var(--bg); color: var(--text); font-family: "Yu Gothic UI", "Hiragino Sans", "Noto Sans JP", Meiryo, sans-serif; line-height: 1.7; }
        header, .steps { max-width: 1100px; margin: 0 auto; padding: 0 16px; }
        header { padding-top: 40px; padding-bottom: 8px; }
        h1 { margin: 0; font-size: 1.7rem; line-height: 1.4; overflow-wrap: anywhere; }
        .meta { margin: 4px 0 0; color: var(--sub); font-size: .9rem; }
        .steps { list-style: none; padding-bottom: 64px; }
        .step { background: var(--card); border: 1px solid var(--line); border-radius: 12px; margin: 20px 0; padding: 16px; scroll-margin-top: 12px; }
        .head { display: flex; gap: 12px; align-items: flex-start; margin-bottom: 12px; }
        .num { flex: none; width: 2rem; height: 2rem; border-radius: 50%; background: var(--accent); color: #fff; font-weight: 700; display: grid; place-items: center; }
        .head p { margin: 0; padding-top: .15rem; font-size: 1.05rem; overflow-wrap: anywhere; }
        .shot { display: block; cursor: zoom-in; }
        .shot img { display: block; width: 100%; height: auto; border: 1px solid var(--line); border-radius: 6px; }
        .zoom { display: none; }
        .zoom.open { display: block; position: fixed; inset: 0; z-index: 10; overflow: auto; background: rgba(0,0,0,.85); cursor: zoom-out; padding: 16px; }
        .zoom.open img { display: block; max-width: none; margin: 0 auto; }
        @media print {
          body { background: #fff; }
          header { padding-top: 0; }
          .step { break-inside: avoid; border: none; padding: 0; }
          .zoom { display: none !important; }
        }

        """;

    // 画像を押すと原寸で表示する。スクリプトが動かない環境でも手順そのものは読める。
    const string Script = """
        document.querySelectorAll('.shot').forEach(function (shot) {
          shot.addEventListener('click', function (e) {
            e.preventDefault();
            var zoom = shot.nextElementSibling;
            zoom.innerHTML = '';
            zoom.appendChild(shot.querySelector('img').cloneNode());
            zoom.classList.add('open');
          });
        });
        document.querySelectorAll('.zoom').forEach(function (zoom) {
          zoom.addEventListener('click', function (e) { e.preventDefault(); zoom.classList.remove('open'); });
        });
        document.addEventListener('keydown', function (e) {
          if (e.key === 'Escape') document.querySelectorAll('.zoom.open').forEach(function (z) { z.classList.remove('open'); });
        });

        """;
}

public static class PdfExporter
{
    const float PageW = 595.28f, PageH = 841.89f, Margin = 42f;
    const float ContentW = PageW - Margin * 2;
    const float Top = Margin, Bottom = PageH - Margin - 14;
    const float Badge = 20f, Gap = 8f, BlockGap = 22f;

    sealed record Block(Step Step, int Number, List<string> Lines, float TextHeight, float ImageW, float ImageH, float Height);

    /// <summary>
    /// A4 縦の PDF を書き出す。文字は選択・検索でき、画像は劣化させずに埋め込む。
    /// <paramref name="caption"/> を外すと、各手順は番号と画像だけになる。
    /// </summary>
    public static void Export(Recording recording, string path, bool caption = true, IProgress<double>? progress = null)
    {
        var title = string.IsNullOrWhiteSpace(recording.Title) ? "手順書" : recording.Title.Trim();
        var meta = $"{recording.CreatedAt:yyyy年M月d日} ・ 全 {recording.Steps.Count} 手順";

        // 文書で使う文字だけを書体から取り出して埋め込む（日本語の書体を丸ごと埋め込むと 10MB を超えるため）
        var subsets = new FontSubsets(recording.Steps.Select(s => caption ? s.Description : "")
            .Concat(Enum.GetValues<ClickKind>().Select(Describer.Verb))
            .Concat(new[] { title, meta, "0123456789 /…" }));
        using var titleText = new TextPainter(20, bold: true, subsets);
        using var metaText = new TextPainter(9, false, subsets);
        using var bodyText = new TextPainter(11, false, subsets);
        using var numberText = new TextPainter(10.5f, bold: true, subsets);

        var titleLines = titleText.Wrap(title, ContentW);
        var headerHeight = titleLines.Count * titleText.LineHeight + metaText.LineHeight + 14;

        // 1) 各手順の大きさを決め、ページに割り付ける
        var pages = new List<List<Block>> { new() };
        var y = Top + headerHeight;
        for (var i = 0; i < recording.Steps.Count; i++)
        {
            var step = recording.Steps[i];
            var lines = caption ? bodyText.Wrap(step.Description, ContentW - Badge - Gap) : new List<string>();
            var textHeight = Math.Max(Badge, lines.Count * bodyText.LineHeight);
            var maxImageH = Bottom - Top - textHeight - Gap;
            var scale = Math.Min(ContentW / step.ImageWidth, maxImageH / step.ImageHeight);
            var block = new Block(step, i + 1, lines, textHeight, step.ImageWidth * scale, step.ImageHeight * scale,
                textHeight + Gap + step.ImageHeight * scale);
            if (y + block.Height > Bottom && pages[^1].Count > 0)
            {
                pages.Add(new());
                y = Top;
            }
            pages[^1].Add(block);
            y += block.Height + BlockGap;
        }

        // 2) 描く
        var metadata = new SKDocumentPdfMetadata
        {
            Title = title,
            Creator = "手順レコーダー",
            Producer = "手順レコーダー",
            Creation = recording.CreatedAt,
            Modified = DateTime.Now,
            RasterDpi = 144,
            EncodingQuality = 101, // 画像を JPEG にせず、劣化なしで埋め込む
        };
        using var stream = new SKFileWStream(path);
        using (var document = SKDocument.CreatePdf(stream, metadata))
        {
            var done = 0;
            for (var p = 0; p < pages.Count; p++)
            {
                using var canvas = document.BeginPage(PageW, PageH);
                y = Top;
                if (p == 0)
                {
                    titleText.DrawLines(canvas, titleLines, Margin, y, new SKColor(0x1D, 0x1D, 0x1F));
                    y += titleLines.Count * titleText.LineHeight;
                    metaText.DrawLine(canvas, meta, Margin, y, new SKColor(0x6E, 0x6E, 0x73));
                    y += metaText.LineHeight + 6;
                    using var rule = new SKPaint { Color = new SKColor(0xD2, 0xD2, 0xD7), StrokeWidth = 0.75f };
                    canvas.DrawLine(Margin, y, PageW - Margin, y, rule);
                    y += 8;
                }

                foreach (var block in pages[p])
                {
                    DrawBlock(canvas, block, y, bodyText, numberText, subsets);
                    y += block.Height + BlockGap;
                    progress?.Report(++done / (double)recording.Steps.Count);
                }

                var footer = $"{p + 1} / {pages.Count}";
                metaText.DrawLine(canvas, footer, (PageW - metaText.Measure(footer)) / 2, PageH - Margin - 4, new SKColor(0x6E, 0x6E, 0x73));
                document.EndPage();
            }
            document.Close();
        }
    }

    static void DrawBlock(SKCanvas canvas, Block block, float y, TextPainter bodyText, TextPainter numberText, FontSubsets subsets)
    {
        using var accent = new SKPaint { Color = Marker.Accent, IsAntialias = true };
        canvas.DrawCircle(Margin + Badge / 2, y + Badge / 2, Badge / 2, accent);
        var label = block.Number.ToString();
        numberText.DrawLine(canvas, label, Margin + (Badge - numberText.Measure(label)) / 2, y + (Badge - numberText.LineHeight) / 2, SKColors.White);
        var textTop = block.Lines.Count == 1 ? y + (Badge - bodyText.LineHeight) / 2 : y;
        bodyText.DrawLines(canvas, block.Lines, Margin + Badge + Gap, textTop, new SKColor(0x1D, 0x1D, 0x1F));

        var left = Margin + (ContentW - block.ImageW) / 2;
        var top = y + block.TextHeight + Gap;
        var k = block.ImageW / block.Step.ImageWidth;

        using var data = SKData.Create(block.Step.ImagePath);
        using var image = SKImage.FromEncodedData(data);
        canvas.Save();
        canvas.Translate(left, top);
        canvas.Scale(k);
        canvas.ClipRect(new SKRect(0, 0, block.Step.ImageWidth, block.Step.ImageHeight));
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        Marker.Draw(canvas, block.Step, subsets);
        canvas.Restore();

        using var border = new SKPaint { Color = new SKColor(0xD2, 0xD2, 0xD7), Style = SKPaintStyle.Stroke, StrokeWidth = 0.5f, IsAntialias = true };
        canvas.DrawRect(new SKRect(left, top, left + block.ImageW, top + block.ImageH), border);
    }
}
