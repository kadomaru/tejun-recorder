namespace Tejun.Core;

/// <summary>クリックの情報から、手順の説明文の初期値を作る。</summary>
public static class Describer
{
    public static string Verb(ClickKind kind) => kind switch
    {
        ClickKind.Right => "右クリック",
        ClickKind.Middle => "中クリック",
        ClickKind.DoubleLeft => "ダブルクリック",
        ClickKind.None => "確認",
        _ => "クリック",
    };

    public static string Describe(ClickKind kind, string? elementName, string? elementType, string? windowTitle)
    {
        var verb = Verb(kind);
        var element = Clean(elementName, 40);
        var window = Clean(windowTitle, 50);
        var type = Clean(elementType, 20);

        if (kind == ClickKind.None)
            return window.Length > 0 ? $"「{window}」の画面を確認します" : "画面を確認します";

        if (element.Length > 0 && element != window)
        {
            var target = type.Length > 0 && !element.Contains(type, StringComparison.OrdinalIgnoreCase)
                ? $"「{element}」{type}"
                : $"「{element}」";
            return $"{target}を{verb}します";
        }

        if (window.Length > 0)
            return $"「{window}」の画面で、印の場所を{verb}します";

        return $"印の場所を{verb}します";
    }

    /// <summary>空白をまとめ、長すぎる文字列を切り詰める。</summary>
    public static string Clean(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (s.Length > max) s = s[..max] + "…";
        return s;
    }
}
