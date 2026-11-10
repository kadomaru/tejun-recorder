using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Tejun.Core;

/// <summary>
/// 使う文字の字形だけを残した書体を作る。
/// 日本語の書体は 1 つで 10MB を超えることがあり、そのまま PDF に埋め込むと小さな手順書でも巨大になるため。
/// TrueType 形式（glyf テーブルを持つ書体）だけを扱い、それ以外や失敗したときは null を返す。
/// 字形の番号は変えないので、文字幅などの情報は元の書体と同じになる。
/// </summary>
public static class FontSubsetter
{
    // 字形の輪郭と幅、ヒント情報だけを残す。組版用の表（GSUB など）や埋め込みビットマップは捨てる。
    static readonly string[] CopiedTables = { "OS/2", "cvt ", "fpgm", "gasp", "hhea", "hmtx", "maxp", "name", "prep" };

    public static SKTypeface? Subset(SKTypeface face, IEnumerable<int> codepoints)
    {
        try
        {
            using var stream = face.OpenStream(out var ttcIndex);
            if (stream == null || stream.Length <= 0) return null;
            var font = new byte[stream.Length];
            if (stream.Read(font, font.Length) != font.Length) return null;

            var used = codepoints.Distinct().Where(cp => cp > 0)
                .Select(cp => (Codepoint: cp, Glyph: (int)face.GetGlyph(cp)))
                .Where(x => x.Glyph != 0)
                .OrderBy(x => x.Codepoint)
                .ToList();
            var bytes = Build(font, ttcIndex, used);
            if (bytes == null) return null;
            using var data = SKData.CreateCopy(bytes);
            var subset = SKTypeface.FromData(data);
            // 念のため、作った書体で同じ字形が引けることを確かめる
            if (subset == null || used.Any(x => subset.GetGlyph(x.Codepoint) != x.Glyph)) return null;
            return subset;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static byte[]? Build(byte[] font, int ttcIndex, IReadOnlyList<(int Codepoint, int Glyph)> used)
    {
        var start = 0;
        if (Tag(font, 0) == "ttcf")
        {
            var count = (int)U32(font, 8);
            if (ttcIndex < 0 || ttcIndex >= count) return null;
            start = (int)U32(font, 12 + ttcIndex * 4);
        }
        var version = U32(font, start);
        if (version != 0x00010000 && version != 0x74727565 /* 'true' */) return null;

        var tables = new Dictionary<string, (int Offset, int Length)>();
        int tableCount = U16(font, start + 4);
        for (var i = 0; i < tableCount; i++)
        {
            var record = start + 12 + i * 16;
            tables[Tag(font, record)] = ((int)U32(font, record + 8), (int)U32(font, record + 12));
        }
        if (!tables.TryGetValue("head", out var head) || !tables.TryGetValue("maxp", out var maxp)
            || !tables.TryGetValue("loca", out var loca) || !tables.TryGetValue("glyf", out var glyf)
            || !tables.ContainsKey("hhea") || !tables.ContainsKey("hmtx"))
            return null;

        int glyphCount = U16(font, maxp.Offset + 4);
        var longLoca = BinaryPrimitives.ReadInt16BigEndian(font.AsSpan(head.Offset + 50)) != 0;
        (int Offset, int Length) Glyph(int id)
        {
            int a, b;
            if (longLoca)
            {
                a = (int)U32(font, loca.Offset + id * 4);
                b = (int)U32(font, loca.Offset + id * 4 + 4);
            }
            else
            {
                a = U16(font, loca.Offset + id * 2) * 2;
                b = U16(font, loca.Offset + id * 2 + 2) * 2;
            }
            return b > a && b <= glyf.Length ? (glyf.Offset + a, b - a) : (0, 0);
        }

        // 残す字形。部品を組み合わせた字形は、部品もたどって残す
        var keep = new SortedSet<int>();
        var pending = new Stack<int>(used.Select(x => x.Glyph));
        pending.Push(0);
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (id < 0 || id >= glyphCount || !keep.Add(id)) continue;
            var (offset, length) = Glyph(id);
            if (length < 10 || BinaryPrimitives.ReadInt16BigEndian(font.AsSpan(offset)) >= 0) continue;
            var p = offset + 10;
            while (p + 4 <= offset + length)
            {
                int flags = U16(font, p);
                pending.Push(U16(font, p + 2));
                p += 4 + ((flags & 0x0001) != 0 ? 4 : 2);
                if ((flags & 0x0008) != 0) p += 2;
                else if ((flags & 0x0040) != 0) p += 4;
                else if ((flags & 0x0080) != 0) p += 8;
                if ((flags & 0x0020) == 0) break;
            }
        }

        // glyf と loca を作り直す（番号はそのまま、使わない字形は長さ 0）
        using var newGlyf = new MemoryStream();
        var newLoca = new byte[(glyphCount + 1) * 4];
        for (var id = 0; id < glyphCount; id++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(id * 4), (uint)newGlyf.Length);
            if (!keep.Contains(id)) continue;
            var (offset, length) = Glyph(id);
            if (length == 0) continue;
            newGlyf.Write(font, offset, length);
            while (newGlyf.Length % 4 != 0) newGlyf.WriteByte(0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(newLoca.AsSpan(glyphCount * 4), (uint)newGlyf.Length);

        var output = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var tag in CopiedTables)
            if (tables.TryGetValue(tag, out var t))
                output[tag] = font.AsSpan(t.Offset, t.Length).ToArray();

        var newHead = font.AsSpan(head.Offset, head.Length).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(newHead.AsSpan(8), 0);  // checkSumAdjustment は最後に入れる
        BinaryPrimitives.WriteInt16BigEndian(newHead.AsSpan(50), 1);  // loca は 4 バイト形式
        output["head"] = newHead;
        output["glyf"] = newGlyf.ToArray();
        output["loca"] = newLoca;
        output["cmap"] = BuildCmap(used);
        output["post"] = BuildPost(font, tables);

        return Assemble(version, output);
    }

    /// <summary>使う文字だけの対応表（形式 12）。</summary>
    static byte[] BuildCmap(IReadOnlyList<(int Codepoint, int Glyph)> used)
    {
        const int header = 4 + 8 * 2;
        var subtableLength = 16 + used.Count * 12;
        var cmap = new byte[header + subtableLength];
        var span = cmap.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 2);            // 表の数
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], 0);            // Unicode
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], 4);            // 全域
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], header);
        BinaryPrimitives.WriteUInt16BigEndian(span[12..], 3);           // Windows
        BinaryPrimitives.WriteUInt16BigEndian(span[14..], 10);          // UCS-4
        BinaryPrimitives.WriteUInt32BigEndian(span[16..], header);
        var sub = span[header..];
        BinaryPrimitives.WriteUInt16BigEndian(sub, 12);
        BinaryPrimitives.WriteUInt32BigEndian(sub[4..], (uint)subtableLength);
        BinaryPrimitives.WriteUInt32BigEndian(sub[12..], (uint)used.Count);
        for (var i = 0; i < used.Count; i++)
        {
            var group = sub[(16 + i * 12)..];
            BinaryPrimitives.WriteUInt32BigEndian(group, (uint)used[i].Codepoint);
            BinaryPrimitives.WriteUInt32BigEndian(group[4..], (uint)used[i].Codepoint);
            BinaryPrimitives.WriteUInt32BigEndian(group[8..], (uint)used[i].Glyph);
        }
        return cmap;
    }

    /// <summary>字形の名前を持たない post テーブル（形式 3）。</summary>
    static byte[] BuildPost(byte[] font, Dictionary<string, (int Offset, int Length)> tables)
    {
        var post = new byte[32];
        if (tables.TryGetValue("post", out var t) && t.Length >= 32)
            font.AsSpan(t.Offset, 32).CopyTo(post);
        BinaryPrimitives.WriteUInt32BigEndian(post, 0x00030000);
        return post;
    }

    static byte[] Assemble(uint version, SortedDictionary<string, byte[]> tables)
    {
        var count = tables.Count;
        var entrySelector = (int)Math.Floor(Math.Log2(count));
        var searchRange = (1 << entrySelector) * 16;
        var offset = 12 + count * 16;
        var total = offset + tables.Values.Sum(t => (t.Length + 3) & ~3);
        var file = new byte[total];
        var span = file.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, version);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)(count * 16 - searchRange));

        var record = 12;
        var headOffset = 0;
        foreach (var (tag, data) in tables)
        {
            data.CopyTo(span[offset..]);
            Encoding.ASCII.GetBytes(tag).CopyTo(span[record..]);
            BinaryPrimitives.WriteUInt32BigEndian(span[(record + 4)..], Checksum(file, offset, (data.Length + 3) & ~3));
            BinaryPrimitives.WriteUInt32BigEndian(span[(record + 8)..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(span[(record + 12)..], (uint)data.Length);
            if (tag == "head") headOffset = offset;
            offset += (data.Length + 3) & ~3;
            record += 16;
        }
        BinaryPrimitives.WriteUInt32BigEndian(span[(headOffset + 8)..], 0xB1B0AFBA - Checksum(file, 0, file.Length));
        return file;
    }

    static uint Checksum(byte[] data, int offset, int length)
    {
        uint sum = 0;
        for (var i = offset; i + 4 <= offset + length; i += 4)
            sum += BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i));
        return sum;
    }

    static string Tag(byte[] data, int offset) => Encoding.ASCII.GetString(data, offset, 4);
    static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
    static ushort U16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
}

/// <summary>ひとまとまりの文書で使う文字を覚えておき、書体ごとに、その文字だけを残した書体を用意する。</summary>
public sealed class FontSubsets
{
    readonly int[] _codepoints;
    readonly Dictionary<string, SKTypeface> _cache = new();

    public FontSubsets(IEnumerable<string> texts)
    {
        var set = new HashSet<int>();
        foreach (var text in texts)
            for (var i = 0; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
                set.Add(char.ConvertToUtf32(text, i));
        _codepoints = set.ToArray();
    }

    /// <summary>絞り込んだ書体。絞り込めない形式の書体は、そのまま返す。</summary>
    public SKTypeface For(SKTypeface face)
    {
        var key = $"{face.FamilyName}|{face.FontWeight}|{face.FontSlant}";
        if (!_cache.TryGetValue(key, out var subset))
            _cache[key] = subset = FontSubsetter.Subset(face, _codepoints) ?? face;
        return subset;
    }
}
