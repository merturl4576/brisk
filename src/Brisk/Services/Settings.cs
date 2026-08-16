using System;
using System.IO;
using System.Text.Json;

namespace Brisk.Services;

public sealed class Settings
{
    public string Language { get; set; } = "system"; // system | en | tr
    public string Theme { get; set; } = "system";    // system | light | dark
    public bool DryRun { get; set; }
    public bool StartWithWindows { get; set; }       // default OFF, on principle

    public static Settings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))
                    ?? new Settings();
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new Settings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
