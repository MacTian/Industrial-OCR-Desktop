// ViewModels/MainViewModel.cs
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PaddleOcrDesktop.Models;
using PaddleOcrDesktop.Services;

namespace PaddleOcrDesktop.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr_debug.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
        System.Diagnostics.Debug.WriteLine($"[OCR] {msg}");
    }

    private readonly OcrService _ocrService;
    private readonly RuleEngine _ruleEngine;
    private readonly ExportService _exportService;
    private readonly ImageViewModel _imageViewModel;
    private readonly ResultViewModel _resultViewModel;

    [ObservableProperty]
    private ObservableCollection<string> _imageFiles = new();

    [ObservableProperty]
    private string? _selectedImageFile;

    [ObservableProperty]
    private bool _isModelLoaded;

    [ObservableProperty]
    private string _modelStatus = "模型未加载";

    [ObservableProperty]
    private Rect? _selectedRoi;

    [ObservableProperty]
    private bool _hasRoi;

    [ObservableProperty]
    private double _batchProgress;

    [ObservableProperty]
    private string _batchStatus = string.Empty;

    public ImageViewModel ImageViewModel => _imageViewModel;
    public ResultViewModel ResultViewModel => _resultViewModel;
    public OcrEngine OcrEngine => _ocrService.OcrEngine;

    public MainViewModel()
    {
        _ruleEngine = new RuleEngine();
        _ruleEngine.LoadRules(RuleEngineConfig.GetDefaultRules());

        var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ppocr_v5_models");
        var ocrEngine = new OcrEngine(modelPath);
        _ocrService = new OcrService(ocrEngine, _ruleEngine);
        _ocrService.ProgressChanged += OnOcrProgressChanged;
        _exportService = new ExportService();
        _imageViewModel = new ImageViewModel();
        _resultViewModel = new ResultViewModel();

        // 启动时自动加载模型（后台执行，异常在 LoadModelAsync 内部处理）
        _ = LoadModelAsync();
    }

    private void OnOcrProgressChanged(object? sender, OcrProgressEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            BatchProgress = (double)e.Current / e.Total;
            BatchStatus = e.Status;
        });
    }

    /// <summary>
    /// 加载 OCR 模型（异步）
    /// </summary>
    [RelayCommand]
    private async Task LoadModelAsync()
    {
        await LoadModelInternalAsync();
    }

    private async Task LoadModelInternalAsync()
    {
        ModelStatus = "正在加载模型...";
        try
        {
            var logs = await Task.Run(() =>
            {
                var result = _ocrService.LoadModel();
                foreach (var log in result)
                    Log(log);
                return result;
            });
            if (logs.Count > 0)
                ModelStatus = logs[^1];
            IsModelLoaded = true;
            Log("模型加载成功");
        }
        catch (Exception ex)
        {
            var msg = $"模型加载失败: {ex.Message}";
            if (ex.InnerException != null)
                msg += $" | {ex.InnerException.Message}";
            ModelStatus = msg;
            Log(msg);
            MessageBox.Show($"OCR模型加载失败。\n\n错误信息: {ex.Message}\n\n日志: {LogFile}",
                "模型加载失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 打开单张图片
    /// </summary>
    [RelayCommand]
    private void OpenImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.tif|所有文件|*.*",
            Title = "选择图片"
        };

        if (dialog.ShowDialog() == true)
        {
            var path = dialog.FileName;
            ImageFiles.Clear();
            ImageFiles.Add(path);
            SelectedImageFile = path;
            _imageViewModel.CurrentImagePath = path;
        }
    }

    /// <summary>
    /// 打开文件夹（批量导入）
    /// </summary>
    [RelayCommand]
    private void OpenFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择图片文件夹"
        };

        if (dialog.ShowDialog() == true)
        {
            var folder = dialog.FolderName;
            var extensions = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif" };
            var files = Directory.GetFiles(folder)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f)
                .ToList();

            ImageFiles.Clear();
            foreach (var file in files)
                ImageFiles.Add(file);

            if (ImageFiles.Count > 0)
            {
                SelectedImageFile = ImageFiles[0];
                _imageViewModel.CurrentImagePath = ImageFiles[0];
            }
        }
    }

    /// <summary>
    /// 选中图片时更新预览
    /// </summary>
    partial void OnSelectedImageFileChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _imageViewModel.CurrentImagePath = value;
            SelectedRoi = null;
            HasRoi = false;
        }
    }

    /// <summary>
    /// 设置 ROI
    /// </summary>
    public void SetRoi(Rect roi)
    {
        SelectedRoi = roi;
        HasRoi = true;
    }

    /// <summary>
    /// 清除 ROI
    /// </summary>
    [RelayCommand]
    private void ClearRoi()
    {
        SelectedRoi = null;
        HasRoi = false;
    }

    /// <summary>
    /// 开始识别当前图片
    /// </summary>
    [RelayCommand]
    private async Task RecognizeAsync()
    {
        if (string.IsNullOrEmpty(SelectedImageFile))
        {
            ResultViewModel.StatusMessage = "请先打开一张图片";
            return;
        }

        // 检查模型是否加载完成
        if (!IsModelLoaded)
        {
            ResultViewModel.StatusMessage = "模型未加载，正在尝试加载...";
            await LoadModelInternalAsync();
        }
        if (!IsModelLoaded)
        {
            MessageBox.Show("OCR模型加载失败，无法进行识别。请检查模型文件是否完整。", "模型加载失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ResultViewModel.StatusMessage = "模型加载失败，无法识别";
            return;
        }

        ResultViewModel.IsRecognizing = true;
        var recognizeSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var fileInfo = new FileInfo(SelectedImageFile);
            Log($"开始识别: {fileInfo.Name} ({fileInfo.Length / 1024}KB)");

            OpenCvSharp.Rect? cvRoi = SelectedRoi.HasValue
                ? new OpenCvSharp.Rect(
                    (int)SelectedRoi.Value.X,
                    (int)SelectedRoi.Value.Y,
                    (int)SelectedRoi.Value.Width,
                    (int)SelectedRoi.Value.Height)
                : null;

            var ocrSw = System.Diagnostics.Stopwatch.StartNew();
            var result = await _ocrService.RecognizeSingleAsync(SelectedImageFile, cvRoi);
            ocrSw.Stop();

            ResultViewModel.SetResult(result);
        }
        catch (Exception ex)
        {
            ResultViewModel.StatusMessage = $"识别异常: {ex.Message}";
            Log($"识别异常: {ex}");
        }
        finally
        {
            ResultViewModel.IsRecognizing = false;
            recognizeSw.Stop();
            Log($"识别总耗时(含UI): {recognizeSw.ElapsedMilliseconds}ms");
        }
    }

    /// <summary>
    /// 批量识别
    /// </summary>
    [RelayCommand]
    private async Task RecognizeBatchAsync()
    {
        if (ImageFiles.Count == 0)
        {
            ResultViewModel.StatusMessage = "请先导入图片";
            return;
        }

        if (!IsModelLoaded)
        {
            ResultViewModel.StatusMessage = "模型未加载，正在尝试加载...";
            await LoadModelInternalAsync();
        }
        if (!IsModelLoaded)
        {
            MessageBox.Show("OCR模型加载失败，无法进行识别。请检查模型文件是否完整。", "模型加载失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ResultViewModel.StatusMessage = "模型加载失败，无法识别";
            return;
        }

        ResultViewModel.IsRecognizing = true;
        BatchStatus = "批量识别中...";

        try
        {
            var results = await _ocrService.RecognizeBatchAsync(ImageFiles.ToList());
            // 显示最后一张的结果，全部结果可导出
            if (results.Count > 0)
            {
                var lastSuccess = results.LastOrDefault(r => r.IsSuccess);
                if (lastSuccess != null)
                    ResultViewModel.SetResult(lastSuccess);
            }
            BatchStatus = $"批量识别完成: {results.Count(r => r.IsSuccess)}/{results.Count} 成功";
        }
        catch (Exception ex)
        {
            BatchStatus = $"批量识别失败: {ex.Message}";
        }
        finally
        {
            ResultViewModel.IsRecognizing = false;
        }
    }

    /// <summary>
    /// 导出结果
    /// </summary>
    [RelayCommand]
    private void ExportResults()
    {
        if (ResultViewModel.CurrentResult == null)
            return;

        var dialog = new SaveFileDialog
        {
            Filter = "Excel 文件|*.xlsx|CSV 文件|*.csv|文本文件|*.txt",
            Title = "导出识别结果",
            FileName = "OCR结果"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var results = new List<OcrResult> { ResultViewModel.CurrentResult };
                var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();

                switch (ext)
                {
                    case ".xlsx":
                        _exportService.ExportToExcel(dialog.FileName, results);
                        break;
                    case ".csv":
                        _exportService.ExportToCsv(dialog.FileName, results);
                        break;
                    case ".txt":
                        _exportService.ExportToTxt(dialog.FileName, results);
                        break;
                }

                MessageBox.Show("导出成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
