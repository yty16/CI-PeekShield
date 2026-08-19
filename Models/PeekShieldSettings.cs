using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CIPeekShield.Models;

public class PeekShieldSettings
{

    private static readonly string _buildToken = "eXR5MTY=";
    internal static string BuildSignature => _buildToken;

    public static string PluginDir =>
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
        ?? AppContext.BaseDirectory;

    public bool EnableSmartPeek { get; set; } = true;

    public bool AutoStartWithClassIsland { get; set; } = true;

    public int CameraIndex { get; set; } = 0;

    public string CameraName { get; set; } = "";

    public int Sensitivity { get; set; } = 1;

    public bool EnableTopBanner { get; set; } = true;
    public bool EnableFullscreenProtect { get; set; } = true;

    public bool ActionPopup { get; set; } = true;
    public bool ActionBlur { get; set; } = true;
    public bool ActionSound { get; set; } = true;
    public bool ActionMinimize { get; set; } = false;

    [JsonConverter(typeof(ProtectedEntryListConverter))]
    public List<ProtectedEntry> ProtectedProcesses { get; set; } = new()
    {
        new ProtectedEntry { Name = "WeChat.exe", Enabled = true },
        new ProtectedEntry { Name = "Weixin.exe", Enabled = true },
        new ProtectedEntry { Name = "qq.exe", Enabled = true },
        new ProtectedEntry { Name = "TIM.exe", Enabled = true },
        new ProtectedEntry { Name = "chrome.exe", Enabled = true },
        new ProtectedEntry { Name = "msedge.exe", Enabled = true },
        new ProtectedEntry { Name = "brave.exe", Enabled = true },
        new ProtectedEntry { Name = "firefox.exe", Enabled = true },
        new ProtectedEntry { Name = "AliWorkbench.exe", Enabled = true },
        new ProtectedEntry { Name = "DingTalk.exe", Enabled = true },
        new ProtectedEntry { Name = "WXWork.exe", Enabled = true },
        new ProtectedEntry { Name = "ClassIsland.exe", Enabled = true },
        new ProtectedEntry { Name = "ClassIsland.Desktop.exe", Enabled = true }
    };

    [JsonConverter(typeof(ProtectedEntryListConverter))]
    public List<ProtectedEntry> ProtectedWindowTitles { get; set; } = new()
    {
        new ProtectedEntry { Name = "桌面", Enabled = true }
    };

    public bool OnlyProtectForeground { get; set; } = true;

    public bool LowLightEnhance { get; set; } = false;

    public bool MirrorPosterFilter { get; set; } = true;

    public bool Paused { get; set; } = false;
    public bool ManualMode { get; set; } = false;

    public bool ShowTrayIcon { get; set; } = true;

    public bool EnableHotkey { get; set; } = true;
    public string HotkeyModifiers { get; set; } = "Ctrl+Shift";
    public string HotkeyKey { get; set; } = "P";
    public bool ScreenshotOnPeek { get; set; } = false;

    public bool RestoreOnSafe { get; set; } = false;

    public bool IsEnrolled { get; set; } = false;

    public int SettingsVersion { get; set; } = 0;

    private static string SettingsPath => Path.Combine(PluginDir, "settings.json");
    public static string SettingsFilePath => SettingsPath;

    public static PeekShieldSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<PeekShieldSettings>(json);
                if (s != null)
                {
                    s.Migrate();
                    return s;
                }
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CI-PeekShield] Settings load error: {ex.Message}");
        }
        return new PeekShieldSettings();
    }

    private void Migrate()
    {
        bool changed = false;
        if (SettingsVersion < 1)
        {
            if (!ProtectedProcesses.Any(p => string.Equals(p.Name, "ClassIsland.exe", StringComparison.OrdinalIgnoreCase)))
            {
                ProtectedProcesses.Add(new ProtectedEntry { Name = "ClassIsland.exe", Enabled = true });
                changed = true;
            }
            if (!ProtectedProcesses.Any(p => string.Equals(p.Name, "ClassIsland.Desktop.exe", StringComparison.OrdinalIgnoreCase)))
            {
                ProtectedProcesses.Add(new ProtectedEntry { Name = "ClassIsland.Desktop.exe", Enabled = true });
                changed = true;
            }
            SettingsVersion = 1;
            changed = true;
        }
        if (changed) Save();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(PluginDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CI-PeekShield] Settings save error: {ex.Message}");
        }
    }
}

public class ProtectedEntry
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public class ProtectedEntryListConverter : JsonConverter<List<ProtectedEntry>>
{
    public override List<ProtectedEntry> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<ProtectedEntry>();
        if (reader.TokenType != JsonTokenType.StartArray) return list;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;
            if (reader.TokenType == JsonTokenType.String)
            {
                list.Add(new ProtectedEntry { Name = reader.GetString() ?? "", Enabled = true });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                string name = "";
                bool enabled = true;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var prop = reader.GetString();
                        reader.Read();
                        if (prop == "Name") name = reader.GetString() ?? "";
                        else if (prop == "Enabled") enabled = reader.GetBoolean();
                    }
                }
                list.Add(new ProtectedEntry { Name = name, Enabled = enabled });
            }
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<ProtectedEntry> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var e in value)
        {
            writer.WriteStartObject();
            writer.WriteString("Name", e.Name);
            writer.WriteBoolean("Enabled", e.Enabled);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
