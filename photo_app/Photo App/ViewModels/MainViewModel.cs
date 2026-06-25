using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SocialPrepTool.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        QueueItems = new ObservableCollection<QueueItem>();
        QueueItems.CollectionChanged += QueueItems_CollectionChanged;
    }

    private void QueueItems_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (QueueItem item in e.OldItems)
            {
                item.PropertyChanged -= QueueItem_PropertyChanged;
            }
        }
        if (e.NewItems != null)
        {
            foreach (QueueItem item in e.NewItems)
            {
                item.PropertyChanged += QueueItem_PropertyChanged;
            }
        }
        OnPropertyChanged(nameof(HasCheckedItems));
    }

    private void QueueItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QueueItem.IsChecked))
        {
            OnPropertyChanged(nameof(HasCheckedItems));
        }
    }

    public bool HasCheckedItems => QueueItems.Any(x => x.IsChecked);

    [ObservableProperty]
    private ObservableCollection<QueueItem> _queueItems;

    [ObservableProperty]
    private QueueItem? _selectedQueueItem;

    [ObservableProperty]
    private Models.PresetContainer _presetContainer = new Models.PresetContainer();

    private readonly Services.PresetLibrary _presetLibrary = new Services.PresetLibrary();

    [RelayCommand]
    private void AddFileToQueue(string filePath)
    {
        var item = new QueueItem(filePath);
        QueueItems.Add(item);
        if (SelectedQueueItem == null)
        {
            SelectedQueueItem = item;
        }
    }

    [RelayCommand]
    private void RemoveFileFromQueue(QueueItem item)
    {
        if (QueueItems.Remove(item))
        {
            if (SelectedQueueItem == item)
            {
                SelectedQueueItem = QueueItems.FirstOrDefault();
            }
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task LoadPresetsAsync()
    {
        try
        {
            var container = await _presetLibrary.LoadPresetsAsync();
            if (container == null)
            {
                container = new Models.PresetContainer();
            }
            if (container.Presets == null)
            {
                container.Presets = new ObservableCollection<Models.Preset>();
            }
            PresetContainer = container;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load presets in VM: {ex}");
            PresetContainer = new Models.PresetContainer();
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SavePresetAsync(Models.Preset preset)
    {
        if (PresetContainer == null)
        {
            PresetContainer = new Models.PresetContainer();
        }
        if (PresetContainer.Presets == null)
        {
            PresetContainer.Presets = new ObservableCollection<Models.Preset>();
        }

        if (!PresetContainer.Presets.Contains(preset))
        {
            PresetContainer.Presets.Add(preset);
        }

        try
        {
            await _presetLibrary.SavePresetsAsync(PresetContainer);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save preset: {ex}");
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task DeletePresetAsync(Models.Preset preset)
    {
        if (PresetContainer == null || PresetContainer.Presets == null) return;

        if (PresetContainer.Presets.Remove(preset))
        {
            try
            {
                await _presetLibrary.SavePresetsAsync(PresetContainer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save presets after delete: {ex}");
            }
        }
    }

    [RelayCommand]
    private void ApplyPreset(Models.Preset preset)
    {
        if (SelectedQueueItem != null)
        {
            SelectedQueueItem.Record.TargetRatio = preset.TargetRatio;
            SelectedQueueItem.Record.BackgroundMode = preset.BackgroundMode;
            SelectedQueueItem.Record.BackgroundColor = preset.BackgroundColor;
            SelectedQueueItem.Record.BlurStrength = preset.BlurStrength;
            SelectedQueueItem.Record.DropShadow = preset.DropShadow;
        }
    }

    [RelayCommand]
    private void ApplyToAll()
    {
        if (SelectedQueueItem == null) return;
        var r = SelectedQueueItem.Record;
        foreach (var item in QueueItems)
        {
            if (item != SelectedQueueItem)
            {
                item.Record.TargetRatio = r.TargetRatio;
                item.Record.BackgroundMode = r.BackgroundMode;
                item.Record.BackgroundColor = r.BackgroundColor;
                item.Record.BlurStrength = r.BlurStrength;
                item.Record.DropShadow = r.DropShadow;
                item.Record.SafeZoneOverlay = r.SafeZoneOverlay;
                item.Record.IsCarousel = r.IsCarousel;
                item.Record.CarouselSlideCount = r.CarouselSlideCount;
            }
        }
    }
}
