using SkiaSharp;
using Tejun.Core;

namespace Tejun.Tests;

/// <summary>テスト用の、画面らしい画像と記録を作る。</summary>
public static class Samples
{
    public static string TempDir(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tejun-tests", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static Step Step(string dir, int w, int h, int x, int y, ClickKind kind, string description, double scale = 1.0)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(0xEC, 0xEF, 0xF4));
            using var bar = new SKPaint { Color = new SKColor(0x2E, 0x34, 0x40) };
            canvas.DrawRect(0, h - 48, w, 48, bar);
            using var window = new SKPaint { Color = SKColors.White };
            canvas.DrawRect(w * 0.1f, h * 0.1f, w * 0.6f, h * 0.6f, window);
            using var button = new SKPaint { Color = new SKColor(0x5E, 0x81, 0xAC) };
            canvas.DrawRect(x - 50, y - 15, 100, 30, button);
        }
        var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".png");
        using (var data = bitmap.Encode(SKEncodedImageFormat.Png, 100))
        using (var file = File.Create(path))
            data.SaveTo(file);
        return new Step
        {
            ImagePath = path, ImageWidth = w, ImageHeight = h, ClickX = x, ClickY = y,
            Kind = kind, Description = description, Scale = scale,
        };
    }

    public static Recording Recording(string dir)
    {
        var r = new Recording { Title = "メールの署名を設定する手順" };
        r.Steps.Add(Step(dir, 1920, 1080, 400, 300, ClickKind.Left, "「設定」ボタンをクリックします"));
        r.Steps.Add(Step(dir, 1920, 1080, 1850, 500, ClickKind.Right, "「受信トレイ」を右クリックします"));
        r.Steps.Add(Step(dir, 2560, 1440, 1200, 700, ClickKind.DoubleLeft, "「署名.txt」をダブルクリックします。長い説明文が複数行に折り返されても、番号と本文の位置がずれないことを確かめるために、わざと長く書いています。Windows の Settings アプリでも同じです。", 1.5));
        r.Steps.Add(Step(dir, 1080, 1920, 500, 1700, ClickKind.Left, "縦長の画面で <OK> & \"保存\" をクリックします 👍"));
        r.Steps.Add(Step(dir, 1920, 1080, 960, 540, ClickKind.None, "「完了」の画面を確認します"));
        return r;
    }
}
