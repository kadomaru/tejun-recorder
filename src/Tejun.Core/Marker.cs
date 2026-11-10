using SkiaSharp;

namespace Tejun.Core;

/// <summary>クリックした場所の印を描く。</summary>
public static class Marker
{
    public static readonly SKColor Accent = new(0xFF, 0x3B, 0x30);

    /// <summary>画像のピクセル単位での印の半径。高解像度の画像でも小さくなりすぎないようにする。</summary>
    public static float Radius(Step step) =>
        (float)Math.Max(16 * step.Scale, Math.Max(step.ImageWidth, step.ImageHeight) / 72.0);

    /// <summary>画像と同じ座標系の canvas に印を描く。</summary>
    public static void Draw(SKCanvas canvas, Step step)
    {
        if (!step.HasMarker) return;
        var r = Radius(step);
        var x = step.ClickX + 0.5f;
        var y = step.ClickY + 0.5f;
        var stroke = Math.Max(2.5f, r * 0.2f);

        using var fill = new SKPaint { Color = Accent.WithAlpha(0x38), IsAntialias = true, Style = SKPaintStyle.Fill };
        using var halo = new SKPaint { Color = SKColors.White.WithAlpha(0xE6), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke * 2f };
        using var ring = new SKPaint { Color = Accent, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke };

        canvas.DrawCircle(x, y, r, fill);
        canvas.DrawCircle(x, y, r, halo);
        canvas.DrawCircle(x, y, r, ring);

        if (step.Kind == ClickKind.DoubleLeft)
        {
            // ダブルクリックは二重の輪で示す
            canvas.DrawCircle(x, y, r * 1.5f, halo);
            canvas.DrawCircle(x, y, r * 1.5f, ring);
        }

        if (step.Kind is ClickKind.Left) return;

        // 左クリック以外は、操作の種類を札で添える
        using var text = new TextPainter(r * 0.8f, bold: true);
        var label = Describer.Verb(step.Kind);
        var padX = r * 0.45f;
        var w = text.Measure(label) + padX * 2;
        var h = text.LineHeight * 1.1f;
        var outer = step.Kind == ClickKind.DoubleLeft ? r * 1.5f : r;
        var left = x + outer + stroke * 2;
        var top = y - h / 2;
        if (left + w > step.ImageWidth) left = x - outer - stroke * 2 - w;
        top = Math.Clamp(top, 0, Math.Max(0, step.ImageHeight - h));

        var rect = new SKRoundRect(new SKRect(left, top, left + w, top + h), h / 2);
        using var bg = new SKPaint { Color = Accent, IsAntialias = true };
        using var edge = new SKPaint { Color = SKColors.White.WithAlpha(0xE6), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke * 0.6f };
        canvas.DrawRoundRect(rect, edge);
        canvas.DrawRoundRect(rect, bg);
        text.DrawLine(canvas, label, left + padX, top + (h - text.LineHeight) / 2, SKColors.White);
    }
}
