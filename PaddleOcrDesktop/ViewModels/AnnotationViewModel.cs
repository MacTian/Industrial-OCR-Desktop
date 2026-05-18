// ViewModels/AnnotationViewModel.cs
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PaddleOcrDesktop.Models;
using PaddleOcrDesktop.Services;

namespace PaddleOcrDesktop.ViewModels;

public partial class AnnotationViewModel : ViewModelBase
{
    private readonly OcrEngine _ocrEngine;
    private readonly AnnotationExportService _exportService;

    [ObservableProperty]
    private AnnotationImage? _currentImage;

    [ObservableProperty]
    private AnnotationRegion? _selectedRegion;

    [ObservableProperty]
    private bool _isDrawingMode;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private ObservableCollection<AnnotationImage> _allAnnotations = new();

    [ObservableProperty]
    private string _annotationCountText = "0 张图片 / 0 个标注";

    /// <summary>
    /// 当前正在绘制的标注区域（未完成）
    /// </summary>
    public AnnotationRegion? CurrentRegion { get; private set; }

    [ObservableProperty]
    private int _currentImageIndex;

    [ObservableProperty]
    private string _imageIndexText = "0 / 0";

    /// <summary>
    /// 切换图片时更新索引和显示文本
    /// </summary>
    private void OnImageChanged()
    {
        ImageIndexText = AllAnnotations.Count > 0
            ? $"{CurrentImageIndex + 1} / {AllAnnotations.Count}"
            : "0 / 0";
    }

    [RelayCommand(CanExecute = nameof(CanGoNextImage))]
    private void NextImage()
    {
        if (AllAnnotations.Count == 0) return;
        CurrentImageIndex = (CurrentImageIndex + 1) % AllAnnotations.Count;
        CurrentImage = AllAnnotations[CurrentImageIndex];
        OnImageChanged();
    }

    private bool CanGoNextImage() => AllAnnotations.Count > 1;

    [RelayCommand(CanExecute = nameof(CanGoPrevImage))]
    private void PrevImage()
    {
        if (AllAnnotations.Count == 0) return;
        CurrentImageIndex = (CurrentImageIndex - 1 + AllAnnotations.Count) % AllAnnotations.Count;
        CurrentImage = AllAnnotations[CurrentImageIndex];
        OnImageChanged();
    }

    private bool CanGoPrevImage() => AllAnnotations.Count > 1;

    public AnnotationViewModel(OcrEngine ocrEngine)
    {
        _ocrEngine = ocrEngine;
        _exportService = new AnnotationExportService();
    }

    // ─── 图片加载 ─────────────────────────────────────────────

    [RelayCommand]
    private void OpenImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif|所有文件|*.*",
            Title = "选择图片",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            LoadImages(dialog.FileNames);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var dialog = new OpenFolderDialog { Title = "选择图片文件夹" };

        if (dialog.ShowDialog() == true)
        {
            var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif" };
            var files = Directory.GetFiles(dialog.FolderName)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToArray();
            LoadImages(files);
        }
    }

    private void LoadImages(string[] fileNames)
    {
        foreach (var file in fileNames)
        {
            if (AllAnnotations.Any(a => a.ImagePath == file)) continue;

            var annotation = new AnnotationImage { ImagePath = file };

            try
            {
                using var bmp = SkiaSharp.SKBitmap.Decode(file);
                if (!bmp.IsEmpty)
                {
                    annotation.ImageWidth = bmp.Width;
                    annotation.ImageHeight = bmp.Height;
                }
            }
            catch { }

            AllAnnotations.Add(annotation);
        }

        if (CurrentImage == null && AllAnnotations.Count > 0)
        {
            CurrentImageIndex = 0;
            CurrentImage = AllAnnotations[0];
        }

        OnImageChanged();
        UpdateAnnotationCount();
        StatusMessage = $"已加载 {AllAnnotations.Count} 张图片";
    }

    // ─── 标注绘制 ─────────────────────────────────────────────

    public void StartNewRegion()
    {
        if (CurrentImage == null) return;
        CurrentRegion = new AnnotationRegion { Id = CurrentImage.NextId };
    }

    public void AddPointToCurrentRegion(System.Windows.Point point)
    {
        CurrentRegion?.Points.Add(point);
    }

    public void FinishCurrentRegion(string text)
    {
        if (CurrentRegion == null || CurrentImage == null) return;
        if (CurrentRegion.Points.Count < 3)
        {
            CancelCurrentRegion();
            return;
        }

        CurrentRegion.Text = text;
        CurrentImage.Regions.Add(CurrentRegion);
        CurrentRegion = null;
        UpdateAnnotationCount();
        OnPropertyChanged(nameof(CurrentImage));
    }

    public void CancelCurrentRegion()
    {
        CurrentRegion = null;
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedRegion != null && CurrentImage != null)
        {
            CurrentImage.Regions.Remove(SelectedRegion);
            SelectedRegion = null;
            UpdateAnnotationCount();
            OnPropertyChanged(nameof(CurrentImage));
        }
    }

    // ─── OCR 预标注 ───────────────────────────────────────────

    [RelayCommand]
    private async Task AutoAnnotate()
    {
        if (CurrentImage == null || !_ocrEngine.IsLoaded)
        {
            StatusMessage = "请先加载图片和 OCR 模型";
            return;
        }

        StatusMessage = "正在进行 OCR 预标注...";

        try
        {
            var result = await Task.Run(() => _ocrEngine.Recognize(CurrentImage.ImagePath));

            if (result.IsSuccess)
            {
                foreach (var region in result.Regions)
                {
                    var annotationRegion = new AnnotationRegion
                    {
                        Id = CurrentImage.NextId,
                        Text = region.Text,
                        Points = new System.Collections.ObjectModel.ObservableCollection<System.Windows.Point>(region.Points ?? System.Array.Empty<System.Windows.Point>())
                    };
                    CurrentImage.Regions.Add(annotationRegion);
                }

                UpdateAnnotationCount();
                OnPropertyChanged(nameof(CurrentImage));
                StatusMessage = $"OCR 预标注完成: {result.Regions.Count} 个区域";
            }
            else
            {
                StatusMessage = $"OCR 预标注失败: {result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"OCR 预标注异常: {ex.Message}";
        }
    }

    // ─── 保存/加载标注项目 ────────────────────────────────────

    [RelayCommand]
    private void SaveAnnotations()
    {
        if (AllAnnotations.Count == 0)
        {
            StatusMessage = "没有标注数据可保存";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "标注项目|*.json",
            Title = "保存标注项目",
            FileName = "annotations"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _exportService.SaveAnnotationProject(dialog.FileName, AllAnnotations.ToList());
                StatusMessage = $"标注项目已保存: {dialog.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
            }
        }
    }

    // ─── 导出训练数据 ─────────────────────────────────────────

    [RelayCommand]
    private void ExportTrainingData()
    {
        if (AllAnnotations.Count == 0)
        {
            StatusMessage = "没有标注数据可导出";
            return;
        }

        var dialog = new OpenFolderDialog { Title = "选择训练数据输出目录" };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var outputDir = dialog.FolderName;
                Directory.CreateDirectory(outputDir);

                // 导出 det 格式
                var detLabelFile = Path.Combine(outputDir, "det_train_label.txt");
                var detImageDir = Path.Combine(outputDir, "images");
                Directory.CreateDirectory(detImageDir);
                _exportService.ExportDetTrainingData(detLabelFile, AllAnnotations.ToList(), detImageDir);

                // 导出 rec 格式
                var recLabelFile = Path.Combine(outputDir, "rec_train_label.txt");
                var recImageDir = Path.Combine(outputDir, "rec_images");
                Directory.CreateDirectory(recImageDir);
                _exportService.ExportRecTrainingData(recLabelFile, recImageDir, AllAnnotations.ToList());

                StatusMessage = $"训练数据已导出到: {outputDir}";
                MessageBox.Show(
                    $"训练数据导出成功！\n\nDet 标签: {detLabelFile}\nRec 标签: {recLabelFile}\n\n目录结构：\n{outputDir}/\n  images/\n  det_train_label.txt\n  rec_images/\n    crop_img/\n  rec_train_label.txt",
                    "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = $"导出失败: {ex.Message}";
            }
        }
    }

    private void UpdateAnnotationCount()
    {
        int imgCount = AllAnnotations.Count;
        int regionCount = AllAnnotations.Sum(a => a.Regions.Count);
        AnnotationCountText = $"{imgCount} 张图片 / {regionCount} 个标注";
    }
}
