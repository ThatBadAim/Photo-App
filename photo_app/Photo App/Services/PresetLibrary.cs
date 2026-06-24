using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SocialPrepTool.Models;

namespace SocialPrepTool.Services;

public class PresetLibrary
{
    private readonly string _presetsFilePath;

    public PresetLibrary()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(appData, "PhotoCrop");
        Directory.CreateDirectory(appFolder);
        _presetsFilePath = Path.Combine(appFolder, "presets.json");
    }

    public async Task<PresetContainer> LoadPresetsAsync()
    {
        if (File.Exists(_presetsFilePath))
        {
            try
            {
                using var stream = File.OpenRead(_presetsFilePath);
                var container = await JsonSerializer.DeserializeAsync<PresetContainer>(stream);
                if (container != null)
                {
                    container.Presets ??= new System.Collections.ObjectModel.ObservableCollection<Preset>();
                    return container;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load presets: {ex}");
            }
        }
        return new PresetContainer();
    }

    public async Task SavePresetsAsync(PresetContainer container)
    {
        using var stream = File.Create(_presetsFilePath);
        await JsonSerializer.SerializeAsync(stream, container, new JsonSerializerOptions { WriteIndented = true });
    }
}
