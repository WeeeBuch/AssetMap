using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetMap.Avalonia.Services;

public class AppSettings
{
    public string  ServerUrl        { get; set; } = "http://localhost:5033";
    public string  ApiKey           { get; set; } = "";
    public bool    IsDarkTheme      { get; set; } = true;
    public string  Accent           { get; set; } = "Blue";
    public string  DisplayCurrency  { get; set; } = "USD";
}

public static class SettingsService
{
    private static readonly string _dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "AssetMap");

    private static readonly string _path = Path.Combine(_dir, "settings.json");

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented           = true,
        DefaultIgnoreCondition  = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppSettings Current { get; private set; } = Load();

    // ── Load ───────────────────────────────────────────────────
    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var text = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<AppSettings>(text, _json) ?? new AppSettings();
            }
        }
        catch { /* první spuštění nebo poškozený soubor → defaults */ }

        return new AppSettings();
    }

    // ── Save ───────────────────────────────────────────────────
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, _json));
        }
        catch { /* disk chyba ignorovat, UI nesmí spadnout */ }
    }
}
