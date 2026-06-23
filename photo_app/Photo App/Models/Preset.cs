using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SocialPrepTool.Models;

public class Preset
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New Preset";
    public string TargetRatio { get; set; } = "4:5";
    public string BackgroundMode { get; set; } = "blur";
    public string BackgroundColor { get; set; } = "#1A1A2E";
    public int BlurStrength { get; set; } = 40;
    public bool DropShadow { get; set; } = false;
}

public class PresetContainer
{
    public ObservableCollection<Preset> Presets { get; set; } = new ObservableCollection<Preset>();
}
