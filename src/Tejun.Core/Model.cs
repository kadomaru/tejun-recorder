namespace Tejun.Core;

public enum ClickKind
{
    Left,
    Right,
    Middle,
    DoubleLeft,
    /// <summary>クリックを伴わない撮影。印は付けない。</summary>
    None,
}

/// <summary>記録した 1 手順。画像は加工していない元のスクリーンショットを PNG のファイルで持つ。</summary>
public sealed class Step
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>元のスクリーンショット（PNG）の場所。印は描き込まれていない。</summary>
    public required string ImagePath { get; init; }

    public int ImageWidth { get; init; }
    public int ImageHeight { get; init; }

    /// <summary>画像の左上を原点としたクリック位置（ピクセル）。</summary>
    public int ClickX { get; init; }
    public int ClickY { get; init; }

    public ClickKind Kind { get; set; } = ClickKind.Left;

    /// <summary>撮影した画面の表示倍率（100% なら 1.0）。印の大きさを合わせるのに使う。</summary>
    public double Scale { get; init; } = 1.0;

    public string Description { get; set; } = "";
    public string WindowTitle { get; set; } = "";
    public string AppName { get; set; } = "";
    public string ElementName { get; set; } = "";
    public string ElementType { get; set; } = "";
    public DateTime Time { get; init; } = DateTime.Now;

    public bool HasMarker => Kind != ClickKind.None;
}

public sealed class Recording
{
    public string Title { get; set; } = "";
    public List<Step> Steps { get; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.Now;
}

public enum CaptureScope
{
    /// <summary>クリックした画面（モニター）全体。</summary>
    ClickedScreen,
    /// <summary>つながっているすべての画面。</summary>
    AllScreens,
    /// <summary>クリックしたウィンドウだけ。</summary>
    ClickedWindow,
}
