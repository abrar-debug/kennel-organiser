using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace kennel;

public class KennelStorage
{
    private readonly string _appRoot;
    private readonly string _shortcutsRoot;
    private readonly string _kennelsFilePath;

    public KennelStorage()
    {
        _appRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kennel Organiser");

        _shortcutsRoot = Path.Combine(_appRoot, "shortcuts");
        _kennelsFilePath = Path.Combine(_appRoot, "kennels.json");
    }

    public List<KennelDefinition> LoadKennels()
    {
        try
        {
            if (!File.Exists(_kennelsFilePath))
                return new List<KennelDefinition>();

            var json = File.ReadAllText(_kennelsFilePath);
            var kennels = JsonSerializer.Deserialize<List<KennelDefinition>>(json, CreateJsonOptions());
            return kennels ?? new List<KennelDefinition>();
        }
        catch
        {
            // Corrupt JSON should not prevent the app from starting.
            return new List<KennelDefinition>();
        }
    }

    public void SaveKennels(IEnumerable<KennelDefinition> kennels)
    {
        Directory.CreateDirectory(_appRoot);

        var options = CreateJsonOptions();
        var json = JsonSerializer.Serialize(kennels.ToList(), options);
        File.WriteAllText(_kennelsFilePath, json);
    }

    public string CopyShortcutToKennel(string kennelId, string originalShortcutPath)
    {
        if (string.IsNullOrWhiteSpace(kennelId))
            throw new ArgumentException("Kennel id is required.", nameof(kennelId));

        if (string.IsNullOrWhiteSpace(originalShortcutPath))
            throw new ArgumentException("Original shortcut path is required.", nameof(originalShortcutPath));

        if (!File.Exists(originalShortcutPath))
            throw new FileNotFoundException("Shortcut not found.", originalShortcutPath);

        var ext = Path.GetExtension(originalShortcutPath).ToLowerInvariant();
        if (ext != ".lnk")
            throw new InvalidOperationException("Only `.lnk` shortcuts are supported.");

        Directory.CreateDirectory(_shortcutsRoot);
        var kennelFolder = Path.Combine(_shortcutsRoot, kennelId);
        Directory.CreateDirectory(kennelFolder);

        var originalFileName = Path.GetFileName(originalShortcutPath);
        var destFileName = $"{Guid.NewGuid():N}_{originalFileName}";
        var destPath = Path.Combine(kennelFolder, destFileName);

        File.Copy(originalShortcutPath, destPath, overwrite: false);
        return destPath;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
        };
    }
}

