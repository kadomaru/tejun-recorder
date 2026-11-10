using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SkiaSharp;
using Tejun.Core;

namespace Tejun.App.Platform.Windows;

/// <summary>
/// マウスのボタンが押された瞬間に（押した結果が画面に出る前に）画面を撮る。
/// 見張りは専用のスレッドで行い、画像の保存は別のスレッドに回してマウスの動きを止めない。
/// </summary>
public sealed class WindowsRecorder : IRecorder
{
    sealed class Pending
    {
        public required RawImage Image;
        public int ClickX, ClickY;
        public ClickKind Kind;
        public double Scale = 1.0;
        public string WindowTitle = "";
        public Task<(string Name, string Type)>? Element;
        public Step? Step;
    }

    public event Action<Step>? StepCaptured;
    public event Action<Step>? StepUpdated;
    public bool IsPaused { get; set; }

    readonly uint _processId = (uint)Environment.ProcessId;
    readonly LowLevelMouseProc _proc;
    RecorderOptions? _options;
    Thread? _hookThread;
    uint _hookThreadId;
    IntPtr _hook;
    BlockingCollection<Pending>? _queue;
    Task? _worker;

    readonly object _clickGate = new();
    Pending? _lastLeft;
    uint _lastLeftTime;
    POINT _lastLeftPoint;

    public WindowsRecorder() => _proc = HookProc;

    public void Start(RecorderOptions options)
    {
        if (_hookThread != null) return;
        _options = options;
        _queue = new BlockingCollection<Pending>();
        var queue = _queue;
        _worker = Task.Factory.StartNew(() => Work(queue), TaskCreationOptions.LongRunning);

        using var ready = new ManualResetEventSlim();
        _hookThread = new Thread(() =>
        {
            _hookThreadId = Native.GetCurrentThreadId();
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
            ready.Set();
            if (_hook == IntPtr.Zero) return;
            while (Native.GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        })
        { IsBackground = true, Name = "TejunMouseHook" };
        _hookThread.Start();
        ready.Wait();
        if (_hook == IntPtr.Zero)
        {
            _hookThread = null;
            throw new InvalidOperationException("マウスの操作を見張る準備ができませんでした。");
        }
    }

    IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && !IsPaused)
        {
            var message = (int)wParam;
            if (message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN)
            {
                try
                {
                    var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    OnMouseDown(message, data);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                }
            }
        }
        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }

    void OnMouseDown(int message, MSLLHOOKSTRUCT data)
    {
        var pt = data.pt;
        var hit = Native.WindowFromPoint(pt);
        var root = hit == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(hit, Native.GA_ROOT);

        // このアプリ自身の窓（記録中の操作バーなど）へのクリックは手順にしない
        if (hit != IntPtr.Zero)
        {
            Native.GetWindowThreadProcessId(hit, out var pid);
            if (pid == _processId) return;
        }

        var kind = message switch
        {
            Native.WM_RBUTTONDOWN => ClickKind.Right,
            Native.WM_MBUTTONDOWN => ClickKind.Middle,
            _ => ClickKind.Left,
        };

        lock (_clickGate)
        {
            if (kind == ClickKind.Left && _lastLeft != null
                && data.time - _lastLeftTime <= Native.GetDoubleClickTime()
                && Math.Abs(pt.X - _lastLeftPoint.X) <= Math.Max(4, Native.GetSystemMetrics(Native.SM_CXDOUBLECLK) / 2)
                && Math.Abs(pt.Y - _lastLeftPoint.Y) <= Math.Max(4, Native.GetSystemMetrics(Native.SM_CYDOUBLECLK) / 2))
            {
                // 2 回目のクリックは新しい手順にせず、直前の手順をダブルクリックに変える
                var previous = _lastLeft;
                _lastLeft = null;
                Step? step;
                lock (previous)
                {
                    previous.Kind = ClickKind.DoubleLeft;
                    step = previous.Step;
                    if (step != null)
                    {
                        step.Kind = ClickKind.DoubleLeft;
                        step.Description = Describer.Describe(step.Kind, step.ElementName, step.ElementType, step.WindowTitle);
                    }
                }
                if (step != null) StepUpdated?.Invoke(step);
                return;
            }
        }

        var pending = Shoot(pt, root, kind);
        if (pending == null) return;

        // 押した部品の名前は、押した結果で画面が変わる前にすぐ調べ始める
        pending.Element = Task.Run(() => Uia.FromPoint(pt.X, pt.Y));

        lock (_clickGate)
        {
            if (kind == ClickKind.Left)
            {
                _lastLeft = pending;
                _lastLeftTime = data.time;
                _lastLeftPoint = pt;
            }
            else
            {
                _lastLeft = null;
            }
        }
        Enqueue(pending);
    }

    Pending? Shoot(POINT pt, IntPtr root, ClickKind kind)
    {
        var scope = _options?.Scope ?? CaptureScope.ClickedScreen;
        var monitor = ScreenCapture.MonitorAt(pt, out var scale);
        var bounds = scope switch
        {
            CaptureScope.AllScreens => ScreenCapture.VirtualScreen(),
            CaptureScope.ClickedWindow => WindowBounds(root, monitor),
            _ => monitor,
        };
        var image = ScreenCapture.Capture(bounds);
        if (image == null) return null;
        return new Pending
        {
            Image = image,
            ClickX = pt.X - bounds.Left,
            ClickY = pt.Y - bounds.Top,
            Kind = kind,
            Scale = scale,
            WindowTitle = root == IntPtr.Zero ? "" : Native.WindowText(root),
        };
    }

    /// <summary>クリックした窓の範囲。デスクトップやタスクバーのように窓らしくないものは画面全体にする。</summary>
    static RECT WindowBounds(IntPtr root, RECT monitor)
    {
        if (root == IntPtr.Zero) return monitor;
        var className = Native.ClassName(root);
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return monitor;

        if (Native.DwmGetWindowAttribute(root, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out var rect, Marshal.SizeOf<RECT>()) != 0
            && !Native.GetWindowRect(root, out rect))
            return monitor;

        var screen = ScreenCapture.VirtualScreen();
        rect.Left = Math.Max(rect.Left, screen.Left);
        rect.Top = Math.Max(rect.Top, screen.Top);
        rect.Right = Math.Min(rect.Right, screen.Right);
        rect.Bottom = Math.Min(rect.Bottom, screen.Bottom);
        // メニューのような小さな窓だけを撮っても前後が分からないので、画面全体にする
        return rect.Width < 200 || rect.Height < 120 ? monitor : rect;
    }

    public void CaptureNow()
    {
        if (_options == null || IsPaused || !Native.GetCursorPos(out var pt)) return;
        var scope = _options.Scope;
        var monitor = ScreenCapture.MonitorAt(pt, out var scale);
        var bounds = scope == CaptureScope.AllScreens ? ScreenCapture.VirtualScreen() : monitor;
        var image = ScreenCapture.Capture(bounds, drawCursor: false);
        if (image == null) return;
        lock (_clickGate) _lastLeft = null;
        Enqueue(new Pending { Image = image, Kind = ClickKind.None, Scale = scale });
    }

    void Enqueue(Pending pending)
    {
        try { _queue?.Add(pending); }
        catch (InvalidOperationException) { /* 停止した直後 */ }
    }

    void Work(BlockingCollection<Pending> queue)
    {
        foreach (var pending in queue.GetConsumingEnumerable())
        {
            try
            {
                var options = _options!;
                var path = Path.Combine(options.SessionFolder, Guid.NewGuid().ToString("N") + ".png");
                SavePng(pending.Image, path);

                var element = ("", "");
                if (pending.Element != null && pending.Element.Wait(TimeSpan.FromMilliseconds(1500)))
                    element = pending.Element.Result;

                Step step;
                lock (pending)
                {
                    step = new Step
                    {
                        ImagePath = path,
                        ImageWidth = pending.Image.Width,
                        ImageHeight = pending.Image.Height,
                        ClickX = pending.ClickX,
                        ClickY = pending.ClickY,
                        Kind = pending.Kind,
                        Scale = pending.Scale,
                        WindowTitle = pending.WindowTitle,
                        ElementName = element.Item1,
                        ElementType = element.Item2,
                    };
                    step.Description = Describer.Describe(step.Kind, step.ElementName, step.ElementType, step.WindowTitle);
                    pending.Step = step;
                }
                StepCaptured?.Invoke(step);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }
    }

    internal static void SavePng(RawImage image, string path)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), image.Width * 4);
            using var file = File.Create(path);
            using var stream = new SKManagedWStream(file);
            // 劣化のない PNG。圧縮は軽めにして、次のクリックに備える
            var options = new SKPngEncoderOptions(SKPngEncoderFilterFlags.Sub | SKPngEncoderFilterFlags.Up | SKPngEncoderFilterFlags.NoFilters, 4);
            if (!pixmap.Encode(stream, options)) throw new IOException("画像を保存できませんでした。");
        }
        finally
        {
            handle.Free();
        }
    }

    public async Task StopAsync()
    {
        if (_hookThread != null)
        {
            Native.PostThreadMessage(_hookThreadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            var thread = _hookThread;
            _hookThread = null;
            await Task.Run(() => thread.Join(2000));
        }
        lock (_clickGate) _lastLeft = null;
        _queue?.CompleteAdding();
        if (_worker != null) await _worker;
        _queue?.Dispose();
        _queue = null;
        _worker = null;
    }

    public void Dispose()
    {
        if (_hookThread != null) Native.PostThreadMessage(_hookThreadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _hookThread = null;
        try { _queue?.CompleteAdding(); } catch (ObjectDisposedException) { }
    }

    /// <summary>指定した窓を、撮影に写らないようにする（記録中の操作バー用）。</summary>
    public static void ExcludeFromCapture(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) Native.SetWindowDisplayAffinity(hwnd, Native.WDA_EXCLUDEFROMCAPTURE);
    }
}
