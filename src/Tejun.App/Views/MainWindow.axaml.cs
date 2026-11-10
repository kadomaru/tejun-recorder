using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Tejun.App.Platform;
using Tejun.Core;
using Tejun.Core.Export;

namespace Tejun.App.Views;

public partial class MainWindow : Window
{
    readonly IRecorder _recorder;
    readonly Settings _settings;
    readonly ObservableCollection<StepViewModel> _steps = new();
    readonly DateTime _createdAt = DateTime.Now;
    string? _sessionFolder;
    RecordingBar? _bar;
    internal RecordingBar? Bar => _bar;
    int _countAtStart;
    bool _recording;
    bool _unsaved;
    bool _closeConfirmed;
    bool _selfTestStarted;
    string? _savedPath;

    public MainWindow() : this(null, null) { }

    public MainWindow(IRecorder? recorder, Settings? settings)
    {
        InitializeComponent();
        _recorder = recorder ?? RecorderFactory.Create();
        _settings = settings ?? Settings.Load();

        StepsList.ItemsSource = _steps;
        _steps.CollectionChanged += (_, _) => Renumber();
        TitleBox.TextChanged += (_, _) => { if (_steps.Count > 0) _unsaved = true; };

        ScopeScreen.IsChecked = _settings.Scope == CaptureScope.ClickedScreen;
        ScopeWindow.IsChecked = _settings.Scope == CaptureScope.ClickedWindow;
        ScopeAll.IsChecked = _settings.Scope == CaptureScope.AllScreens;

        if (_recorder is DemoRecorder)
            HomeNote.Text = "この環境は Windows ではないため、見本の手順で動作を確かめるモードで動いています。";

        _recorder.StepCaptured += step => Dispatcher.UIThread.Post(() => OnStepCaptured(step));
        _recorder.StepUpdated += step => Dispatcher.UIThread.Post(() => OnStepUpdated(step));

        Closing += OnClosing;
        Opened += OnOpened;
        Closed += (_, _) => _recorder.Dispose();
        Renumber();
    }

    void OnOpened(object? sender, EventArgs e)
    {
        // 「--selftest 出力先」で起動したときは、実際にクリックを起こして記録から保存までを確かめる
        var args = Program.Args;
        var index = Array.IndexOf(args, "--selftest");
        // Opened は窓を出し直すたびに来るので、最初の 1 回だけ動かす
        if (index >= 0 && index + 1 < args.Length && OperatingSystem.IsWindows() && !_selfTestStarted)
        {
            _selfTestStarted = true;
            _ = SelfTest.RunAsync(this, args[index + 1]);
        }
    }

    public IReadOnlyList<StepViewModel> Steps => _steps;
    public bool IsRecording => _recording;

    CaptureScope SelectedScope =>
        ScopeWindow.IsChecked == true ? CaptureScope.ClickedWindow
        : ScopeAll.IsChecked == true ? CaptureScope.AllScreens
        : CaptureScope.ClickedScreen;

    // ---- 記録 ----

    async void OnStartClick(object? sender, RoutedEventArgs e) => await StartRecordingAsync();
    async void OnContinueClick(object? sender, RoutedEventArgs e) => await StartRecordingAsync();

    public async Task StartRecordingAsync()
    {
        if (_recording) return;
        _recording = true;
        _settings.Scope = SelectedScope;
        _settings.Save();
        _sessionFolder ??= RecorderFactory.NewSessionFolder();
        _countAtStart = _steps.Count;

        // この窓が写り込まないよう、隠し終わってから見張りをはじめる
        Hide();
        await Task.Delay(350);

        try
        {
            _recorder.IsPaused = false;
            _recorder.Start(new RecorderOptions(_settings.Scope, _sessionFolder));
        }
        catch (Exception ex)
        {
            _recording = false;
            Show();
            ShowStatus("記録をはじめられませんでした：" + ex.Message, null, error: true);
            ShowEditorOrHome();
            return;
        }

        _bar = new RecordingBar();
        _bar.SetCount(0);
        _bar.PauseChanged += paused => _recorder.IsPaused = paused;
        _bar.ShotRequested += () =>
        {
            if (_recorder is DemoRecorder demo) demo.AddScripted();
            else _recorder.CaptureNow();
        };
        _bar.StopRequested += async () => await StopRecordingAsync();
        // 操作バーを閉じたときも、記録を終えて編集画面へ進む
        _bar.Closing += async (_, args) =>
        {
            if (!_recording) return;
            args.Cancel = true;
            await StopRecordingAsync();
        };
        _bar.Show();
    }

    public async Task StopRecordingAsync()
    {
        if (!_recording) return;
        _recording = false;
        if (_bar != null) _bar.IsEnabled = false;
        await _recorder.StopAsync();
        // 保存が終わった手順の通知を、画面に反映し終えるまで待つ
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        _bar?.Close();
        _bar = null;
        Show();
        WindowState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
        Activate();
        ShowEditorOrHome(forceEditor: true);
        if (_steps.Count > _countAtStart)
        {
            HideStatus();
            var first = _steps[_countAtStart];
            Dispatcher.UIThread.Post(() => StepsList.ScrollIntoView(_steps.IndexOf(first)), DispatcherPriority.Background);
        }
    }

    void OnStepCaptured(Step step)
    {
        var vm = new StepViewModel(step);
        vm.Edited += () => _unsaved = true;
        _steps.Add(vm);
        _unsaved = true;
        _bar?.SetCount(_steps.Count - _countAtStart);
    }

    void OnStepUpdated(Step step) => _steps.FirstOrDefault(s => ReferenceEquals(s.Step, step))?.Refresh();

    // ---- 編集 ----

    void Renumber()
    {
        for (var i = 0; i < _steps.Count; i++) _steps[i].SetPosition(i + 1, _steps.Count);
        CountText.Text = $"全 {_steps.Count} 手順";
        var any = _steps.Count > 0;
        SavePdfButton.IsEnabled = SaveHtmlButton.IsEnabled = SavePngButton.IsEnabled = any;
        EmptyNote.IsVisible = !any;
        StepsScroll.IsVisible = any;
    }

    void ShowEditorOrHome(bool forceEditor = false)
    {
        var editor = forceEditor || _steps.Count > 0;
        EditorView.IsVisible = editor;
        HomeView.IsVisible = !editor;
    }

    static StepViewModel? StepOf(object? sender) => (sender as Control)?.DataContext as StepViewModel;

    void OnMoveUpClick(object? sender, RoutedEventArgs e) => Move(StepOf(sender), -1);
    void OnMoveDownClick(object? sender, RoutedEventArgs e) => Move(StepOf(sender), +1);

    internal void Move(StepViewModel? step, int delta)
    {
        if (step == null) return;
        var from = _steps.IndexOf(step);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= _steps.Count) return;
        _steps.Move(from, to);
        _unsaved = true;
    }

    void OnDeleteClick(object? sender, RoutedEventArgs e) => Delete(StepOf(sender));

    internal void Delete(StepViewModel? step)
    {
        if (step != null && _steps.Remove(step)) _unsaved = true;
    }

    async void OnNewClick(object? sender, RoutedEventArgs e)
    {
        if (_steps.Count > 0 && !await ConfirmDialog.Ask(this,
                _unsaved ? "まだ保存していない記録があります。\n記録を捨てて、最初からやり直しますか？"
                         : "今の記録を閉じて、最初からやり直しますか？",
                "やり直す", "やめておく"))
            return;
        _steps.Clear();
        TitleBox.Text = "";
        _unsaved = false;
        HideStatus();
        ShowEditorOrHome();
    }

    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closeConfirmed || !_unsaved || _steps.Count == 0) return;
        e.Cancel = true;
        if (await ConfirmDialog.Ask(this, "まだ保存していない記録があります。\n保存せずに終了しますか？", "保存せずに終了", "戻る"))
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // ---- 保存 ----

    public Recording BuildRecording()
    {
        var recording = new Recording { Title = (TitleBox.Text ?? "").Trim(), CreatedAt = _createdAt };
        recording.Steps.AddRange(_steps.Select(s => s.Step));
        return recording;
    }

    async Task<IStorageFolder?> StartFolderAsync()
    {
        var path = _settings.LastFolder;
        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            return await StorageProvider.TryGetFolderFromPathAsync(path);
        return await StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents);
    }

    async void OnSavePdfClick(object? sender, RoutedEventArgs e) =>
        await SaveFileAsync("PDF", "pdf", "application/pdf", (r, path, p) => PdfExporter.Export(r, path, p));

    async void OnSaveHtmlClick(object? sender, RoutedEventArgs e) =>
        await SaveFileAsync("HTML", "html", "text/html", (r, path, p) => HtmlExporter.Export(r, path, p));

    async Task SaveFileAsync(string label, string extension, string mime, Action<Recording, string, IProgress<double>> export)
    {
        var recording = BuildRecording();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"{label} で保存",
            SuggestedFileName = FileNames.Safe(recording.Title, "手順書") + "." + extension,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await StartFolderAsync(),
            FileTypeChoices = new[]
            {
                new FilePickerFileType($"{label} ファイル") { Patterns = new[] { "*." + extension }, MimeTypes = new[] { mime } },
            },
        });
        var path = file?.TryGetLocalPath();
        if (path == null) return;
        await RunExportAsync($"{label} を保存しています…", path, p => export(recording, path, p));
    }

    async void OnSavePngClick(object? sender, RoutedEventArgs e)
    {
        var recording = BuildRecording();
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "PNG 画像の保存先のフォルダーを選ぶ",
            AllowMultiple = false,
            SuggestedStartLocation = await StartFolderAsync(),
        });
        var parent = folders.FirstOrDefault()?.TryGetLocalPath();
        if (parent == null) return;

        // 選んだ場所の中に、タイトルの名前でフォルダーを作ってまとめる
        var name = FileNames.Safe(recording.Title, "手順書");
        var folder = Path.Combine(parent, name);
        for (var i = 2; Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any(); i++)
            folder = Path.Combine(parent, $"{name} ({i})");
        await RunExportAsync("PNG 画像を保存しています…", folder, p => PngExporter.Export(recording, folder, true, p));
    }

    public async Task<bool> RunExportAsync(string message, string path, Action<IProgress<double>> export)
    {
        BusyText.Text = message;
        BusyBar.Value = 0;
        BusyView.IsVisible = true;
        try
        {
            var progress = new Progress<double>(v => BusyBar.Value = v);
            await Task.Run(() => export(progress));
            _unsaved = false;
            _settings.LastFolder = Path.GetDirectoryName(path);
            _settings.Save();
            ShowStatus("保存しました：" + path, path);
            return true;
        }
        catch (Exception ex)
        {
            ShowStatus("保存できませんでした：" + ex.Message, null, error: true);
            return false;
        }
        finally
        {
            BusyView.IsVisible = false;
        }
    }

    void ShowStatus(string text, string? path, bool error = false)
    {
        _savedPath = path;
        StatusText.Text = text;
        StatusBox.Background = Avalonia.Media.Brush.Parse(error ? "#FDECEA" : "#E8F5EC");
        OpenSavedButton.IsVisible = path != null;
        StatusBox.IsVisible = true;
    }

    void HideStatus() => StatusBox.IsVisible = false;

    void OnOpenSavedClick(object? sender, RoutedEventArgs e)
    {
        if (_savedPath == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_savedPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowStatus("開けませんでした：" + ex.Message, null, error: true);
        }
    }
}
