using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Tejun.App.Platform.Windows;

namespace Tejun.App.Views;

/// <summary>記録中だけ画面の下に出る、小さな操作バー。</summary>
public partial class RecordingBar : Window
{
    public event Action? StopRequested;
    public event Action? ShotRequested;
    public event Action<bool>? PauseChanged;

    bool _paused;
    readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public RecordingBar()
    {
        InitializeComponent();
        _blink.Tick += (_, _) => Dot.Opacity = _paused ? 1 : (Dot.Opacity > 0.6 ? 0.35 : 1);
        Opened += OnOpened;
        Closed += (_, _) => _blink.Stop();
    }

    void OnOpened(object? sender, EventArgs e)
    {
        _blink.Start();
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle)
            WindowsRecorder.ExcludeFromCapture(handle.Handle);

        // 主画面の下の中央に置く
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null) return;
        var area = screen.WorkingArea;
        var size = PixelSize.FromSize(ClientSize, screen.Scaling);
        Position = new PixelPoint(area.X + (area.Width - size.Width) / 2, area.Y + area.Height - size.Height - (int)(28 * screen.Scaling));
    }

    public void SetCount(int count) => CountText.Text = $"{count} 手順";

    void OnDragStart(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    void OnPauseClick(object? sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseButton.Content = _paused ? "記録を再開" : "一時停止";
        StateText.Text = _paused ? "一時停止中" : "記録中";
        Dot.Fill = _paused ? new SolidColorBrush(Color.Parse("#8E8E93")) : (IBrush)this.FindResource("Accent")!;
        Dot.Opacity = 1;
        ShotButton.IsEnabled = !_paused;
        PauseChanged?.Invoke(_paused);
    }

    void OnShotClick(object? sender, RoutedEventArgs e) => ShotRequested?.Invoke();
    void OnStopClick(object? sender, RoutedEventArgs e) => StopRequested?.Invoke();
}
