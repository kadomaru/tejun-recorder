using System.Text;
using SkiaSharp;
using Tejun.App.Platform.Windows;
using Tejun.App.Views;
using Tejun.Core;
using Tejun.Core.Export;

namespace Tejun.App;

/// <summary>
/// Windows の上で実際にマウスのクリックを起こし、記録・編集画面への反映・保存までを通して確かめる。
/// 自動テスト用（--selftest 出力先フォルダー）。結果は selftest.log と終了コードで返す。
/// </summary>
internal static class SelfTest
{
    const uint LeftDown = 0x0002, LeftUp = 0x0004, RightDown = 0x0008, RightUp = 0x0010;

    public static async Task RunAsync(MainWindow window, string folder)
    {
        Directory.CreateDirectory(folder);
        var log = new StringBuilder();
        var failures = new List<string>();
        void Log(string line) { log.AppendLine(line); File.WriteAllText(Path.Combine(folder, "selftest.log"), log.ToString()); }
        void Check(bool ok, string what) { Log((ok ? "OK   " : "FAIL ") + what); if (!ok) failures.Add(what); }

        var code = 0;
        try
        {
            await Task.Delay(2000);
            var screen = ScreenCapture.VirtualScreen();
            Log($"virtual screen: {screen.Left},{screen.Top} {screen.Width}x{screen.Height}");
            SaveScreen(Path.Combine(folder, "screen-1-home.png"));

            await window.StartRecordingAsync();
            await Task.Delay(1000);
            Check(window.IsRecording, "記録がはじまる");
            // 操作バーは撮影に写らない設定なので、見た目を確かめる 1 枚のときだけ一時的に写るようにする
            var barHandle = window.Bar?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            Check(barHandle != IntPtr.Zero, "操作バーが出る");
            Native.SetWindowDisplayAffinity(barHandle, 0);
            await Task.Delay(300);
            SaveScreen(Path.Combine(folder, "screen-2-recording.png"));
            WindowsRecorder.ExcludeFromCapture(barHandle);
            await Task.Delay(300);

            int X(double f) => screen.Left + (int)(screen.Width * f);
            int Y(double f) => screen.Top + (int)(screen.Height * f);

            // 1) 左クリック
            Click(X(0.40), Y(0.40), LeftDown, LeftUp);
            await Task.Delay(1200);
            // 2) ダブルクリック（2 回で 1 つの手順になるはず）
            Click(X(0.62), Y(0.50), LeftDown, LeftUp);
            await Task.Delay(80);
            Click(X(0.62), Y(0.50), LeftDown, LeftUp);
            await Task.Delay(1200);
            // 3) 右クリック（出たメニューは Esc で閉じる）
            Click(X(0.30), Y(0.62), RightDown, RightUp);
            await Task.Delay(800);
            Key(0x1B);
            await Task.Delay(600);
            // 4) タスクバーの上（部品の名前が取れるかを見る）
            Click(X(0.50), screen.Bottom - 20, LeftDown, LeftUp);
            await Task.Delay(1200);
            Key(0x1B);
            await Task.Delay(600);

            await window.StopRecordingAsync();
            await Task.Delay(2500);
            SaveScreen(Path.Combine(folder, "screen-3-editor.png"));

            var steps = window.Steps.Select(s => s.Step).ToList();
            foreach (var s in steps)
                Log($"step: {s.Kind} at {s.ClickX},{s.ClickY} image {s.ImageWidth}x{s.ImageHeight} scale {s.Scale} window=[{s.WindowTitle}] element=[{s.ElementName}] type=[{s.ElementType}] -> {s.Description}");

            Check(steps.Count == 4, $"手順が 4 つできる（実際は {steps.Count}）");
            if (steps.Count == 4)
            {
                Check(steps[0].Kind == ClickKind.Left, "1 つ目は左クリック");
                Check(steps[1].Kind == ClickKind.DoubleLeft, "2 つ目はダブルクリック");
                Check(steps[2].Kind == ClickKind.Right, "3 つ目は右クリック");
                Check(steps[0].ClickX == X(0.40) - screen.Left && steps[0].ClickY == Y(0.40) - screen.Top, "クリックした位置が画素単位で合っている");
            }
            foreach (var (s, i) in steps.Select((s, i) => (s, i)))
            {
                using var bitmap = SKBitmap.Decode(s.ImagePath);
                Check(bitmap != null && bitmap.Width == s.ImageWidth && bitmap.Height == s.ImageHeight, $"手順 {i + 1} の画像が読める");
                if (bitmap == null) continue;
                var colors = new HashSet<uint>();
                for (var y = 0; y < bitmap.Height; y += 16)
                for (var x = 0; x < bitmap.Width; x += 16)
                    colors.Add((uint)bitmap.GetPixel(x, y));
                Check(colors.Count > 1, $"手順 {i + 1} の画像に画面が写っている（色の数 {colors.Count}）");
                File.Copy(s.ImagePath, Path.Combine(folder, $"raw-{i + 1}.png"), true);
            }

            var recording = window.BuildRecording();
            recording.Title = "自己診断の手順書";
            var pdf = Path.Combine(folder, "out.pdf");
            var html = Path.Combine(folder, "out.html");
            Check(await window.RunExportAsync("PDF", pdf, p => PdfExporter.Export(recording, pdf, p)) && new FileInfo(pdf).Length > 1000, "PDF で保存できる");
            var pdfSize = new FileInfo(pdf).Length;
            Check(pdfSize < 3_000_000, $"PDF が大きくなりすぎない（{pdfSize:N0} バイト）");
            Check(await window.RunExportAsync("HTML", html, p => HtmlExporter.Export(recording, html, p)) && new FileInfo(html).Length > 1000, "HTML で保存できる");
            var png = Path.Combine(folder, "png");
            Check(await window.RunExportAsync("PNG", png, p => PngExporter.Export(recording, png, true, p)) && Directory.GetFiles(png, "*.png").Length == steps.Count, "PNG で保存できる");
            await Task.Delay(500);
            SaveScreen(Path.Combine(folder, "screen-4-saved.png"));

            code = failures.Count == 0 ? 0 : 1;
            Log(failures.Count == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + failures.Count + ")");
        }
        catch (Exception e)
        {
            Log("EXCEPTION: " + e);
            code = 2;
        }
        Environment.Exit(code);
    }

    static void Click(int x, int y, uint down, uint up)
    {
        Native.SetCursorPos(x, y);
        Thread.Sleep(120);
        Native.mouse_event(down, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(40);
        Native.mouse_event(up, 0, 0, 0, UIntPtr.Zero);
    }

    static void Key(byte vk)
    {
        Native.keybd_event(vk, 0, 0, UIntPtr.Zero);
        Native.keybd_event(vk, 0, 2, UIntPtr.Zero);
    }

    static void SaveScreen(string path)
    {
        var image = ScreenCapture.Capture(ScreenCapture.VirtualScreen());
        if (image != null) WindowsRecorder.SavePng(image, path);
    }
}
