using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using Tejun.Core;
using Tejun.Core.Export;

namespace Tejun.App;

/// <summary>編集画面に並べる手順 1 つ分。</summary>
public sealed class StepViewModel : INotifyPropertyChanged
{
    const int PreviewWidth = 1100;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Edited;

    public Step Step { get; }

    public StepViewModel(Step step) => Step = step;

    int _number;
    public int Number
    {
        get => _number;
        set { if (_number != value) { _number = value; Raise(nameof(Number)); } }
    }

    public string Description
    {
        get => Step.Description;
        set
        {
            if (Step.Description == value) return;
            Step.Description = value ?? "";
            Raise(nameof(Description));
            Edited?.Invoke();
        }
    }

    bool _isFirst, _isLast;
    public bool CanMoveUp { get => !_isFirst; }
    public bool CanMoveDown { get => !_isLast; }

    public void SetPosition(int number, int count)
    {
        Number = number;
        _isFirst = number == 1;
        _isLast = number == count;
        Raise(nameof(CanMoveUp));
        Raise(nameof(CanMoveDown));
    }

    Bitmap? _preview;
    bool _loading;

    /// <summary>印を描き込んだ、表示用の縮小画像。画面に出るときに初めて作る。</summary>
    public Bitmap? Preview
    {
        get
        {
            if (_preview == null && !_loading)
            {
                _loading = true;
                _ = LoadPreviewAsync();
            }
            return _preview;
        }
    }

    /// <summary>手順の内容（クリックの種類など）が変わったので、表示用の画像を作り直す。</summary>
    public void Refresh()
    {
        Raise(nameof(Description));
        if (_preview != null || _loading)
        {
            _loading = true;
            _ = LoadPreviewAsync();
        }
    }

    async Task LoadPreviewAsync()
    {
        try
        {
            var bitmap = await Task.Run(() => RenderPreview(Step));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _preview = bitmap;
                _loading = false;
                Raise(nameof(Preview));
            });
        }
        catch (Exception)
        {
            _loading = false;
        }
    }

    public static Bitmap RenderPreview(Step step)
    {
        using var full = StepImage.Render(step);
        var scale = Math.Min(1.0, PreviewWidth / (double)full.Width);
        var w = Math.Max(1, (int)Math.Round(full.Width * scale));
        var h = Math.Max(1, (int)Math.Round(full.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque));
        surface.Canvas.DrawImage(full, new SKRect(0, 0, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 80);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
