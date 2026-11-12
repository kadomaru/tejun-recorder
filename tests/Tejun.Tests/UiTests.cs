using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Tejun.App;
using Tejun.App.Platform;
using Tejun.App.Views;
using Tejun.Core;
using Tejun.Core.Export;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Tejun.Tests.TestAppBuilder))]

namespace Tejun.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Tejun.App.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>画面を表示せずにアプリを動かし、記録から保存までの流れを通す。</summary>
public class UiTests
{
    /// <summary>
    /// 非同期の処理が終わるまで、UI スレッドの仕事をこちらで回しながら待つ。
    /// 画面なしのモードでは、別スレッドからの通知だけでは UI スレッドが動かないため。
    /// </summary>
    static void Wait(Task task)
    {
        for (var i = 0; i < 3000 && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(task.IsCompleted, "処理が終わりませんでした");
        task.GetAwaiter().GetResult();
    }

    static void Settle(int ms = 150)
    {
        Wait(Task.Delay(ms));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    static void Shot(Window window, string name)
    {
        if (string.IsNullOrEmpty(Artifacts.Dir)) return;
        Directory.CreateDirectory(Artifacts.Dir);
        using var frame = window.CaptureRenderedFrame();
        if (frame == null) return;
        using var file = File.Create(Path.Combine(Artifacts.Dir, name));
        frame.Save(file);
    }

    [Fact]
    public async Task RecordEditAndExport()
    {
        // セッションは破棄しない（破棄が戻ってこないため。テストの終了とともに片付く）
        var session = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
        await session.Dispatch(() =>
        {
            var dir = Samples.TempDir("ui");
            var demo = new DemoRecorder();
            var settings = new Settings();
            var window = new MainWindow(demo, settings);
            window.Show();
            Settle();
            Shot(window, "ui-1-home.png");

            Wait(window.StartRecordingAsync());
            Assert.True(window.IsRecording);
            Assert.False(window.IsVisible);
            for (var i = 0; i < 4; i++) demo.AddScripted();
            demo.CaptureNow();
            Settle();
            Assert.NotNull(window.Bar);
            Shot(window.Bar!, "ui-2-bar.png");

            Wait(window.StopRecordingAsync());
            Assert.True(window.IsVisible);
            Assert.Null(window.Bar);
            Assert.Equal(5, window.Steps.Count);
            Assert.Equal("「スタート」ボタンをクリックします", window.Steps[0].Description);
            Assert.Equal(ClickKind.DoubleLeft, window.Steps[3].Step.Kind);
            Assert.False(window.Steps[0].CanMoveUp);
            Assert.False(window.Steps[4].CanMoveDown);
            window.FindControl<TextBox>("TitleBox")!.Text = "報告書を開く手順";

            for (var i = 0; i < 50 && window.Steps.Take(2).Any(s => s.Preview == null); i++) Settle(100);
            Settle(200);
            Shot(window, "ui-3-editor.png");

            var recording = window.BuildRecording();
            Assert.Equal("報告書を開く手順", recording.Title);
            var pdf = Path.Combine(dir, "out.pdf");
            var export = window.RunExportAsync("PDF を保存しています…", pdf, p => PdfExporter.Export(recording, pdf, true, p));
            Wait(export);
            Assert.True(export.Result);
            Assert.True(new FileInfo(pdf).Length > 10_000);
            Settle();
            Shot(window, "ui-4-saved.png");

            // 「説明文を入れる」を外すと、次に開いたときにも引き継ぐ設定に残る
            var check = window.FindControl<CheckBox>("CaptionCheck")!;
            Assert.True(check.IsChecked);
            check.IsChecked = false;
            Assert.False(window.IncludeDescription);
            Assert.False(settings.IncludeDescription);
            check.IsChecked = true;
            Artifacts.Keep(pdf, "ui.pdf");

            // 並べ替えと削除で、番号が振り直される
            var second = window.Steps[1];
            window.Move(second, -1);
            Assert.Same(second, window.Steps[0]);
            Assert.Equal(1, second.Number);
            Assert.Equal(2, window.Steps[1].Number);
            window.Delete(second);
            Assert.Equal(4, window.Steps.Count);
            Assert.Equal(new[] { 1, 2, 3, 4 }, window.Steps.Select(s => s.Number));
            Assert.Equal(4, window.BuildRecording().Steps.Count);
            // 保存していない変更があっても、テストでは確認の窓を出さずに閉じられるよう全部消す
            foreach (var step in window.Steps.ToList()) window.Delete(step);
            Settle();
            Shot(window, "ui-5-empty.png");
            window.Close();

            StoreShots();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Microsoft Store に載せるスクリーンショットを撮る（出力先 TEJUN_ARTIFACTS があるときだけ）。
    /// 記録済みの画面として、見本の業務画面（packaging/store/screens）を読み込ませる。
    /// </summary>
    static void StoreShots()
    {
        if (string.IsNullOrEmpty(Artifacts.Dir)) return;
        var screens = FindUp(Path.Combine("packaging", "store", "screens"));
        if (screens == null) return;

        var recorder = new ScreenFileRecorder(screens);
        // Store が求める大きさ（1366 x 768 以上）に合わせる
        var window = new MainWindow(recorder, new Settings()) { Width = 1366, Height = 768 };
        window.Show();
        Settle();
        Shot(window, "store-1-home.png");

        Wait(window.StartRecordingAsync());
        recorder.EmitAll();
        Settle();
        Wait(window.StopRecordingAsync());
        Assert.Equal(3, window.Steps.Count);
        window.FindControl<TextBox>("TitleBox")!.Text = "経費を申請する手順";
        for (var i = 0; i < 50 && window.Steps.Take(2).Any(s => s.Preview == null); i++) Settle(100);
        Settle(200);
        Shot(window, "store-2-editor.png");

        var recording = window.BuildRecording();
        var folder = Path.Combine(Samples.TempDir("store"), "png");
        var export = window.RunExportAsync("PNG 画像を保存しています…", folder, p => PngExporter.Export(recording, folder, true, p));
        Wait(export);
        Assert.True(export.Result);
        var files = Directory.GetFiles(folder, "*.png").OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.Equal(3, files.Length);
        for (var i = 0; i < files.Length; i++) Artifacts.Keep(files[i], $"store-{3 + i}-exported.png");

        foreach (var step in window.Steps.ToList()) window.Delete(step);
        window.Close();
    }

    static string? FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>用意した画像を、記録した画面として順に渡す記録係。</summary>
    sealed class ScreenFileRecorder(string screens) : IRecorder
    {
        public event Action<Step>? StepCaptured;
        public event Action<Step>? StepUpdated { add { } remove { } }
        public bool IsPaused { get; set; }

        RecorderOptions? _options;

        // クリック位置は packaging/store/make-screens.py の絵に合わせてある
        static readonly (string File, int X, int Y, ClickKind Kind, string Element, string Type)[] Script =
        {
            ("screen-1.png", 365, 188, ClickKind.Left, "新規申請", "ボタン"),
            ("screen-2.png", 1214, 752, ClickKind.Left, "申請する", "ボタン"),
            ("screen-3.png", 960, 306, ClickKind.DoubleLeft, "展示会の見学（電車）", "項目"),
        };

        public void Start(RecorderOptions options) => _options = options;

        public void CaptureNow() { }

        public void EmitAll()
        {
            foreach (var s in Script)
            {
                var path = Path.Combine(_options!.SessionFolder, s.File);
                File.Copy(Path.Combine(screens, s.File), path, true);
                StepCaptured?.Invoke(new Step
                {
                    ImagePath = path, ImageWidth = 1920, ImageHeight = 1080, ClickX = s.X, ClickY = s.Y, Kind = s.Kind,
                    WindowTitle = "経費精算（サンプル）", ElementName = s.Element, ElementType = s.Type,
                    Description = Describer.Describe(s.Kind, s.Element, s.Type, "経費精算（サンプル）"),
                });
            }
        }

        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
}
