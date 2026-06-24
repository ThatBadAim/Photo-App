using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media.Imaging;
using SocialPrepTool.Models;

namespace SocialPrepTool.ViewModels;

public enum QueueItemStatus
{
    Pending,
    Processing,
    Done,
    Error
}

public partial class QueueItem : ObservableObject
{
    public QueueItem(string filePath)
    {
        Id = Guid.NewGuid().ToString();
        Record = new EditRecord { SourceFilePath = filePath };
        Status = QueueItemStatus.Pending;
        FileName = CapitalizeFileName(System.IO.Path.GetFileName(filePath));
        LoadThumbnail(filePath);
    }

    public string Id { get; }

    [ObservableProperty]
    private EditRecord _record;

    [ObservableProperty]
    private QueueItemStatus _status;

    [ObservableProperty]
    private string _fileName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaceholderVisibility))]
    [NotifyPropertyChangedFor(nameof(ThumbnailVisibility))]
    private BitmapImage? _thumbnail;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isChecked = false;

    public Microsoft.UI.Xaml.Visibility PlaceholderVisibility => Thumbnail == null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility ThumbnailVisibility => Thumbnail != null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string TruncatedFileName
    {
        get
        {
            if (string.IsNullOrEmpty(FileName)) return "";
            if (FileName.Length <= 20) return FileName;
            return FileName.Substring(0, 17) + "...";
        }
    }

    partial void OnFileNameChanged(string value)
    {
        OnPropertyChanged(nameof(TruncatedFileName));
    }

    private async void LoadThumbnail(string filePath)
    {
        try
        {
            if (!System.IO.File.Exists(filePath)) return;

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
            var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);

            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue != null)
            {
                dispatcherQueue.TryEnqueue(async () =>
                {
                    using (stream)
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.DecodePixelWidth = 64;
                            bitmap.DecodePixelHeight = 64;
                            await bitmap.SetSourceAsync(stream);
                            Thumbnail = bitmap;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error setting thumbnail source: {ex}");
                        }
                    }
                });
            }
            else
            {
                using (stream)
                {
                    var bitmap = new BitmapImage();
                    bitmap.DecodePixelWidth = 64;
                    bitmap.DecodePixelHeight = 64;
                    await bitmap.SetSourceAsync(stream);
                    Thumbnail = bitmap;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading thumbnail: {ex}");
        }
    }

    private static string CapitalizeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return fileName;
        string name = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string ext = System.IO.Path.GetExtension(fileName);
        char[] chars = name.ToCharArray();
        bool newWord = true;
        for (int i = 0; i < chars.Length; i++)
        {
            if (char.IsLetter(chars[i]))
            {
                if (newWord)
                {
                    chars[i] = char.ToUpper(chars[i]);
                    newWord = false;
                }
            }
            else if (chars[i] == ' ' || chars[i] == '_' || chars[i] == '-')
            {
                newWord = true;
            }
        }
        return new string(chars) + ext;
    }
}
