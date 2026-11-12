using System.Text.Json;
using Tejun.Core;

namespace Tejun.App;

/// <summary>次に開いたときにも引き継ぐ設定。</summary>
public sealed class Settings
{
    public CaptureScope Scope { get; set; } = CaptureScope.ClickedScreen;
    public string? LastFolder { get; set; }
    /// <summary>保存するとき、各手順に説明文を入れるか。</summary>
    public bool IncludeDescription { get; set; } = true;

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TejunRecorder", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception) { /* 壊れていたら初期値で始める */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception) { /* 保存できなくても動作は続ける */ }
    }
}
