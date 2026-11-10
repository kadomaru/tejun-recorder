using Tejun.Core;

namespace Tejun.App.Platform;

public sealed record RecorderOptions(CaptureScope Scope, string SessionFolder);

/// <summary>クリックを見張り、そのたびに画面を撮って手順にする。</summary>
public interface IRecorder : IDisposable
{
    /// <summary>手順が 1 つできた。UI スレッド以外から呼ばれる。</summary>
    event Action<Step>? StepCaptured;

    /// <summary>すでに通知した手順の内容が変わった（ダブルクリックと分かった、など）。</summary>
    event Action<Step>? StepUpdated;

    bool IsPaused { get; set; }

    void Start(RecorderOptions options);

    /// <summary>クリックなしで、今の画面を手順として撮る。</summary>
    void CaptureNow();

    /// <summary>見張りをやめ、撮影済みの画像の保存が終わるまで待つ。</summary>
    Task StopAsync();
}

public static class RecorderFactory
{
    public static IRecorder Create() =>
        OperatingSystem.IsWindows() ? new Windows.WindowsRecorder() : new DemoRecorder();

    public static string NewSessionFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "TejunRecorder");
        Directory.CreateDirectory(root);
        // 前回までの作業用フォルダーのうち、古いものを片付ける
        try
        {
            foreach (var dir in Directory.GetDirectories(root))
                if (Directory.GetLastWriteTime(dir) < DateTime.Now.AddDays(-3))
                    Directory.Delete(dir, true);
        }
        catch (Exception) { /* 使用中のものは残す */ }

        var folder = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(folder);
        return folder;
    }
}
