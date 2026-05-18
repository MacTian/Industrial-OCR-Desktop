// ViewModels/TrainingViewModel.cs
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PaddleOcrDesktop.Models;
using PaddleOcrDesktop.Services;

namespace PaddleOcrDesktop.ViewModels;

public partial class TrainingViewModel : ViewModelBase
{
    private readonly TrainingService _trainingService;

    [ObservableProperty]
    private TrainingConfig _config = new();

    [ObservableProperty]
    private string _paddleocrDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "PaddleOCR");

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private string _trainingLog = string.Empty;

    [ObservableProperty]
    private bool _isTrainingRunning;

    [ObservableProperty]
    private double _trainingProgress;

    private System.Diagnostics.Process? _trainingProcess;

    public TrainingViewModel()
    {
        _trainingService = new TrainingService();
        Config.DataDir = Path.Combine(Environment.CurrentDirectory, "train_data");
        Config.OutputDir = Path.Combine(Environment.CurrentDirectory, "output");
        Config.LabelFilePath = Path.Combine(Environment.CurrentDirectory, "train_data", "det_train_label.txt");
    }

    // ─── 浏览命令 ─────────────────────────────────────────────

    [RelayCommand]
    private void BrowseDataDir()
    {
        var dialog = new OpenFolderDialog { Title = "选择训练数据目录" };
        if (dialog.ShowDialog() == true)
            Config.DataDir = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseLabelFile()
    {
        var dialog = new OpenFileDialog { Filter = "标签文件|*.txt|所有文件|*.*", Title = "选择标签文件" };
        if (dialog.ShowDialog() == true)
            Config.LabelFilePath = dialog.FileName;
    }

    [RelayCommand]
    private void BrowseOutputDir()
    {
        var dialog = new OpenFolderDialog { Title = "选择输出目录" };
        if (dialog.ShowDialog() == true)
            Config.OutputDir = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowsePaddleocrDir()
    {
        var dialog = new OpenFolderDialog { Title = "选择 PaddleOCR 仓库目录" };
        if (dialog.ShowDialog() == true)
            PaddleocrDir = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseDictFile()
    {
        var dialog = new OpenFileDialog { Filter = "字典文件|*.txt|所有文件|*.*", Title = "选择字典文件" };
        if (dialog.ShowDialog() == true)
            Config.DictFilePath = dialog.FileName;
    }

    // ─── 训练控制 ─────────────────────────────────────────────

    [RelayCommand]
    private void CheckEnvironment()
    {
        var (ok, message) = _trainingService.CheckEnvironment(PaddleocrDir);
        StatusMessage = message;
        MessageBox.Show(message, ok ? "检查通过" : "检查失败",
            MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    [RelayCommand]
    private void GenerateConfig()
    {
        if (string.IsNullOrEmpty(Config.DataDir))
        {
            StatusMessage = "请先设置数据目录";
            return;
        }

        try
        {
            var yaml = _trainingService.GenerateTrainingConfigYaml(Config);
            var configPath = Path.Combine(Config.OutputDir, $"train_config_{Config.Mode.ToString().ToLower()}.yaml");
            Directory.CreateDirectory(Config.OutputDir);
            File.WriteAllText(configPath, yaml);
            StatusMessage = $"配置已生成: {configPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"生成配置失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StartTraining()
    {
        if (IsTrainingRunning)
        {
            StatusMessage = "训练已在运行中";
            return;
        }

        if (string.IsNullOrEmpty(Config.DataDir) || string.IsNullOrEmpty(Config.LabelFilePath))
        {
            StatusMessage = "请先设置数据目录和标签文件";
            return;
        }

        try
        {
            // 先生成配置文件
            Directory.CreateDirectory(Config.OutputDir);
            var configPath = Path.Combine(Config.OutputDir, $"train_config_{Config.Mode.ToString().ToLower()}.yaml");
            var yaml = _trainingService.GenerateTrainingConfigYaml(Config);
            File.WriteAllText(configPath, yaml);

            IsTrainingRunning = true;
            var now = System.DateTime.Now;
            TrainingLog = $"[{now:HH:mm:ss}] 启动训练...\n";
            TrainingLog += $"[{now:HH:mm:ss}] 配置: {configPath}\n";
            TrainingLog += $"[{now:HH:mm:ss}] 设备: {(Config.Device == TrainingDevice.GPU ? $"GPU {Config.GpuDeviceId}" : "CPU")}\n";
            TrainingLog += $"[{now:HH:mm:ss}] 模式: {Config.Mode}\n";
            TrainingLog += $"[{now:HH:mm:ss}] PaddleOCR: {PaddleocrDir}\n\n";

            _trainingProcess = _trainingService.StartTraining(Config, configPath, PaddleocrDir);

            if (_trainingProcess == null)
            {
                StatusMessage = "启动训练进程失败";
                IsTrainingRunning = false;
                return;
            }

            // 异步读取输出
            _ = Task.Run(() =>
            {
                while (!_trainingProcess.HasExited)
                {
                    var line = _trainingProcess.StandardOutput.ReadLine();
                    if (line != null)
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            TrainingLog += line + "\n";
                        });
                    }
                }

                var exitCode = _trainingProcess.ExitCode;
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    IsTrainingRunning = false;
                    StatusMessage = $"训练完成，退出码: {exitCode}";
                    TrainingLog += $"\n[{System.DateTime.Now:HH:mm:ss}] 训练结束，退出码: {exitCode}\n";
                });
            });

            StatusMessage = "训练已启动";
        }
        catch (Exception ex)
        {
            IsTrainingRunning = false;
            StatusMessage = $"启动训练失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private void StopTraining()
    {
        if (_trainingProcess != null && !_trainingProcess.HasExited)
        {
            try
            {
                _trainingProcess.Kill();
                TrainingLog += $"\n[{System.DateTime.Now:HH:mm:ss}] 训练已手动停止\n";
                StatusMessage = "训练已停止";
            }
            catch (Exception ex)
            {
                StatusMessage = $"停止失败: {ex.Message}";
            }
        }
        IsTrainingRunning = false;
    }
}
