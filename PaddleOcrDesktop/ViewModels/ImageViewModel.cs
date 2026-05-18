// ViewModels/ImageViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;

namespace PaddleOcrDesktop.ViewModels;

public partial class ImageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _currentImagePath = string.Empty;

    partial void OnCurrentImagePathChanged(string value)
    {
        OnPropertyChanged(nameof(HasImage));
    }

    public bool HasImage => !string.IsNullOrEmpty(CurrentImagePath) && System.IO.File.Exists(CurrentImagePath);
}
