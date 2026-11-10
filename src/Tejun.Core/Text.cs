using SkiaSharp;

namespace Tejun.Core;

/// <summary>日本語を表示できる書体を探し、折り返しと描画を行う。</summary>
public sealed class TextPainter : IDisposable
{
    static readonly string[] Families =
    {
        "Yu Gothic UI", "Meiryo UI", "Meiryo", "Yu Gothic",
        "MS UI Gothic", "MS Gothic",
        "Noto Sans CJK JP", "Noto Sans JP", "Hiragino Sans", "IPAGothic",
    };

    static readonly Lazy<SKTypeface> RegularFace = new(() => Find(SKFontStyle.Normal));
    static readonly Lazy<SKTypeface> BoldFace = new(() => Find(SKFontStyle.Bold));

    static SKTypeface Find(SKFontStyle style)
    {
        var manager = SKFontManager.Default;
        foreach (var family in Families)
        {
            var face = manager.MatchFamily(family, style);
            // 見つからないときに既定の書体を返す環境があるので、名前と日本語の字形の両方を確かめる
            if (face != null && face.FamilyName.Contains(family.Split(' ')[0], StringComparison.OrdinalIgnoreCase)
                && face.ContainsGlyph('あ'))
                return face;
        }
        return manager.MatchCharacter(null, style, new[] { "ja" }, 'あ')
            ?? manager.MatchCharacter('あ')
            ?? SKTypeface.Default;
    }

    readonly SKFont _font;
    readonly Dictionary<int, SKFont?> _fallbacks = new();

    public TextPainter(float size, bool bold = false)
    {
        _font = new SKFont(bold ? BoldFace.Value : RegularFace.Value, size)
        {
            Subpixel = true,
            Edging = SKFontEdging.Antialias,
        };
    }

    public float Size => _font.Size;
    public float LineHeight => _font.Size * 1.5f;

    /// <summary>書体ごとに分けた文字列の並び。基本の書体に無い文字だけ別の書体で描く。</summary>
    IEnumerable<(string Text, SKFont Font)> Runs(string text)
    {
        var start = 0;
        SKFont current = _font;
        var i = 0;
        while (i < text.Length)
        {
            var cp = char.ConvertToUtf32(text, i);
            var len = char.IsSurrogatePair(text, i) ? 2 : 1;
            var font = FontFor(cp);
            if (!ReferenceEquals(font, current))
            {
                if (i > start) yield return (text[start..i], current);
                start = i;
                current = font;
            }
            i += len;
        }
        if (text.Length > start) yield return (text[start..], current);
    }

    SKFont FontFor(int codepoint)
    {
        if (codepoint < 0x80 || _font.Typeface.ContainsGlyph(codepoint)) return _font;
        if (!_fallbacks.TryGetValue(codepoint, out var fallback))
        {
            var face = SKFontManager.Default.MatchCharacter(codepoint);
            fallback = face == null ? null : new SKFont(face, _font.Size) { Subpixel = true, Edging = SKFontEdging.Antialias };
            _fallbacks[codepoint] = fallback;
        }
        return fallback ?? _font;
    }

    public float Measure(string text)
    {
        var width = 0f;
        foreach (var (run, font) in Runs(text)) width += font.MeasureText(run);
        return width;
    }

    /// <summary>幅に収まるように行へ分ける。英単語は途中で切らず、日本語は文字単位で折り返す。</summary>
    public List<string> Wrap(string text, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = "";
            foreach (var token in Tokens(paragraph))
            {
                var candidate = line + token;
                if (line.Length == 0 || Measure(candidate.TrimEnd()) <= maxWidth)
                {
                    line = candidate;
                    // 1 語だけで幅を超えるときは文字単位で切る
                    while (Measure(line.TrimEnd()) > maxWidth && line.Length > 1)
                    {
                        var cut = line.Length - 1;
                        while (cut > 1 && Measure(line[..cut]) > maxWidth) cut--;
                        if (char.IsLowSurrogate(line[cut]) && cut > 1) cut--;
                        lines.Add(line[..cut]);
                        line = line[cut..];
                    }
                }
                else
                {
                    lines.Add(line.TrimEnd());
                    line = token.TrimStart();
                }
            }
            lines.Add(line.TrimEnd());
        }
        return lines;
    }

    static IEnumerable<string> Tokens(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            if (IsWordChar(text[i]))
            {
                while (i < text.Length && IsWordChar(text[i])) i++;
            }
            else
            {
                i += char.IsSurrogatePair(text, i) ? 2 : 1;
                // 行頭に来てはいけない句読点や閉じ括弧は前の文字に付ける
                while (i < text.Length && "、。，．」』）)]｝】・ー！？!?,.:;".Contains(text[i])) i++;
            }
            while (i < text.Length && text[i] == ' ') i++;
            yield return text[start..i];
        }
    }

    static bool IsWordChar(char c) => c > ' ' && c < 0x2E80 && !char.IsSurrogate(c);

    /// <summary>左上を (x, y) として 1 行を描く。</summary>
    public void DrawLine(SKCanvas canvas, string text, float x, float y, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var metrics = _font.Metrics;
        var baseline = y + (LineHeight - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent;
        foreach (var (run, font) in Runs(text))
        {
            canvas.DrawText(run, x, baseline, SKTextAlign.Left, font, paint);
            x += font.MeasureText(run);
        }
    }

    public void DrawLines(SKCanvas canvas, IReadOnlyList<string> lines, float x, float y, SKColor color)
    {
        foreach (var line in lines)
        {
            DrawLine(canvas, line, x, y, color);
            y += LineHeight;
        }
    }

    public void Dispose()
    {
        _font.Dispose();
        foreach (var f in _fallbacks.Values) f?.Dispose();
    }
}
