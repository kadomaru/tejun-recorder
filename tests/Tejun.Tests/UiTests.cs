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
            var window = new MainWindow(demo, new Settings());
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
            var export = window.RunExportAsync("PDF を保存しています…", pdf, p => PdfExporter.Export(recording, pdf, p));
            Wait(export);
            Assert.True(export.Result);
            Assert.True(new FileInfo(pdf).Length > 10_000);
            Settle();
            Shot(window, "ui-4-saved.png");
            Artifacts.Keep(pdf, "ui.pdf");
            window.Close();
        }, CancellationToken.None);
    }
}
