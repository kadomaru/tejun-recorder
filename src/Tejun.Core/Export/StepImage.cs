using SkiaSharp;

namespace Tejun.Core.Export;

/// <summary>手順 1 つ分の画像を作る。</summary>
public static class StepImage
{
    public static SKBitmap Load(Step step) =>
        SKBitmap.Decode(step.ImagePath) ?? throw new IOException($"画像を読み込めませんでした: {step.ImagePath}");

    /// <summary>印を描き込んだ画像。<paramref name="caption"/> があれば、番号と説明の帯を上に付ける。</summary>
    public static SKImage Render(Step step, int number = 0, bool caption = false)
    {
        using var source = Load(step);
        var w = source.Width;
        var h = source.Height;

        var fontSize = (float)Math.Max(15 * step.Scale, w / 75.0);
        using var text = new TextPainter(fontSize);
        using var numberText = new TextPainter(fontSize * 0.9f, bold: true);
        var pad = fontSize * 0.9f;
        var badge = fontSize * 1.9f;
        List<string>? lines = null;
        var stripHeight = 0;
        if (caption)
        {
            lines = text.Wrap(step.Description, w - pad * 3 - badge);
            stripHeight = (int)Math.Ceiling(Math.Max(badge, lines.Count * text.LineHeight) + pad * 2);
        }

        using var surface = SKSurface.Create(new SKImageInfo(w, h + stripHeight, SKColorType.Rgba8888, SKAlphaType.Opaque));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        if (caption && lines != null)
        {
            using var accent = new SKPaint { Color = Marker.Accent, IsAntialias = true };
            var cy = pad + badge / 2;
            canvas.DrawCircle(pad + badge / 2, cy, badge / 2, accent);
            var label = number.ToString();
            numberText.DrawLine(canvas, label, pad + (badge - numberText.Measure(label)) / 2, cy - numberText.LineHeight / 2, SKColors.White);
            var textHeight = lines.Count * text.LineHeight;
            var textTop = lines.Count == 1 ? cy - text.LineHeight / 2 : pad + Math.Max(0, (badge - textHeight) / 2);
            text.DrawLines(canvas, lines, pad * 2 + badge, textTop, new SKColor(0x1D, 0x1D, 0x1F));
            using var line = new SKPaint { Color = new SKColor(0xD2, 0xD2, 0xD7), StrokeWidth = 1 };
            canvas.DrawLine(0, stripHeight - 0.5f, w, stripHeight - 0.5f, line);
        }

        canvas.Save();
        canvas.Translate(0, stripHeight);
        canvas.DrawBitmap(source, 0, 0);
        Marker.Draw(canvas, step);
        canvas.Restore();

        return surface.Snapshot();
    }

    public static byte[] RenderPng(Step step, int number = 0, bool caption = false)
    {
        using var image = Render(step, number, caption);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
