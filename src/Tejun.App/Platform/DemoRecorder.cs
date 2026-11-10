using SkiaSharp;
using Tejun.Core;

namespace Tejun.App.Platform;

/// <summary>Windows 以外で画面の作りを確かめるための、見本の手順を作る記録係。</summary>
public sealed class DemoRecorder : IRecorder
{
    public event Action<Step>? StepCaptured;
    public event Action<Step>? StepUpdated { add { } remove { } }
    public bool IsPaused { get; set; }

    RecorderOptions? _options;
    int _count;

    static readonly (int X, int Y, ClickKind Kind, string Element, string Type, string Window)[] Script =
    {
        (96, 1056, ClickKind.Left, "スタート", "ボタン", "タスク バー"),
        (640, 420, ClickKind.Left, "設定", "ボタン", "スタート"),
        (1500, 300, ClickKind.Right, "ドキュメント", "項目", "エクスプローラー"),
        (900, 600, ClickKind.DoubleLeft, "報告書.docx", "項目", "エクスプローラー"),
    };

    public void Start(RecorderOptions options) => _options = options;

    public void CaptureNow() => Add(ClickKind.None, 960, 540, "", "", "デスクトップ");

    public void AddScripted()
    {
        var s = Script[_count % Script.Length];
        Add(s.Kind, s.X, s.Y, s.Element, s.Type, s.Window);
    }

    void Add(ClickKind kind, int x, int y, string element, string type, string window)
    {
        if (_options == null || IsPaused) return;
        _count++;
        const int w = 1920, h = 1080;
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h),
                new[] { new SKColor(0x1B, 0x6C, 0xA8), new SKColor(0x6F, 0xC3, 0xDF) }, SKShaderTileMode.Clamp);
            using var bg = new SKPaint { Shader = shader };
            canvas.DrawRect(0, 0, w, h, bg);
            using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var gray = new SKPaint { Color = new SKColor(0xF0, 0xF2, 0xF5), IsAntialias = true };
            using var dark = new SKPaint { Color = new SKColor(0x20, 0x24, 0x2C) };
            canvas.DrawRoundRect(300 + _count * 20, 120 + _count * 10, 1100, 720, 10, 10, white);
            canvas.DrawRect(300 + _count * 20, 160 + _count * 10, 1100, 2, gray);
            canvas.DrawRect(0, h - 48, w, 48, dark);
            using var button = new SKPaint { Color = new SKColor(0x2F, 0x6F, 0xED), IsAntialias = true };
            canvas.DrawRoundRect(x - 70, y - 18, 140, 36, 6, 6, button);
            using var text = new TextPainter(16);
            if (element.Length > 0) text.DrawLine(canvas, element, x - text.Measure(element) / 2, y - text.LineHeight / 2, SKColors.White);
        }
        var path = Path.Combine(_options.SessionFolder, Guid.NewGuid().ToString("N") + ".png");
        using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
        using (var file = File.Create(path))
            data.SaveTo(file);

        StepCaptured?.Invoke(new Step
        {
            ImagePath = path, ImageWidth = w, ImageHeight = h, ClickX = x, ClickY = y, Kind = kind,
            WindowTitle = window, ElementName = element, ElementType = type,
            Description = Describer.Describe(kind, element, type, window),
        });
    }

    public Task StopAsync() => Task.CompletedTask;
    public void Dispose() { }
}
