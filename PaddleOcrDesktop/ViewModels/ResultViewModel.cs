// ViewModels/ResultViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PaddleOcrDesktop.Models;

namespace PaddleOcrDesktop.ViewModels;

public partial class ResultViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<TextRegion> _regions = new();

    [ObservableProperty]
    private OcrResult? _currentResult;

    [ObservableProperty]
    private bool _isRecognizing;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    public void SetResult(OcrResult result)
    {
        CurrentResult = result;
        Regions.Clear();
        foreach (var region in result.Regions)
        {
            Regions.Add(region);
        }
        // 手动触发 Regions 属性变化通知（集合内容变化不会自动触发 PropertyChanged）
        OnPropertyChanged(nameof(Regions));
        StatusMessage = result.IsSuccess
            ? $"识别完成: {result.Regions.Count} 个区域, {result.ImageWidth}x{result.ImageHeight}, 耗时 {result.ElapsedMilliseconds}ms"
            : $"识别失败: {result.ErrorMessage}";
        System.Diagnostics.Debug.WriteLine($"[OCR] {StatusMessage}");
    }

    public void Clear()
    {
        CurrentResult = null;
        Regions.Clear();
        StatusMessage = "就绪";
        IsRecognizing = false;
    }
}
