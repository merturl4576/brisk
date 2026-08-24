using System;
using System.IO;
using System.Text.Json;

namespace Brisk.Services;

public sealed class Settings
{
    public string Language { get; set; } = "system"; // system | en | tr
    // system | light | dark. Dark is the default on purpose: the navy
    // cockpit IS the product's face, so a fresh install (or a settings file
    // from before this key existed) opens dark. Anyone who explicitly picked
    // light or system has that value in their JSON and keeps it.
    public string Theme { get; set; } = "dark";
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
