using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SocialPrepTool.Models;

public partial class EditRecord : ObservableObject
{
    [ObservableProperty]
    private string _sourceFilePath = string.Empty;

    [ObservableProperty]
    private int _manualRotationDegrees = 0;

    [ObservableProperty]
    private double _cropAnchorX = 0.5;

    [ObservableProperty]
    private double _cropAnchorY = 0.5;

    [ObservableProperty]
    private string _targetRatio = "4:5";

    [ObservableProperty]
    private string _backgroundMode = "blur";

    [ObservableProperty]
    private string _backgroundColor = "#1A1A2E";

    [ObservableProperty]
    private int _blurStrength = 40;

    [ObservableProperty]
    private bool _dropShadow = false;

    [ObservableProperty]
    private bool _safeZoneOverlay = false;

    [ObservableProperty]
    private bool _isCarousel = false;

    [ObservableProperty]
    private int _carouselSlideCount = 3;

    public EditRecord Clone()
    {
        return new EditRecord
        {
            SourceFilePath = this.SourceFilePath,
            ManualRotationDegrees = this.ManualRotationDegrees,
            CropAnchorX = this.CropAnchorX,
            CropAnchorY = this.CropAnchorY,
            TargetRatio = this.TargetRatio,
            BackgroundMode = this.BackgroundMode,
            BackgroundColor = this.BackgroundColor,
            BlurStrength = this.BlurStrength,
            DropShadow = this.DropShadow,
            SafeZoneOverlay = this.SafeZoneOverlay,
            IsCarousel = this.IsCarousel,
            CarouselSlideCount = this.CarouselSlideCount
        };
    }
}
