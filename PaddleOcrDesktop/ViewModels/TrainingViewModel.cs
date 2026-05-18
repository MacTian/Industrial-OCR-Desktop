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

    [ObservableProperty]
    private bool _isConfiguring;

    [ObservableProperty]
    private string _configureLog = string.Empty;

    [ObservableProperty]
    private string _currentConfigureStep = string.Empty;

    [ObservableProperty]
    private bool _configureDownloadPretrained = true;

    [ObservableProperty]
    private bool _configureInstallDeps = true;

    [ObservableProperty]
    private bool _configureInstallPaddle = true;

    /// <summary>
    /// PaddlePaddle 安装版本：true=CPU, false=GPU
    /// </summary>
    [ObservableProperty]
    private bool _configurePaddleCpu = true;

    public TrainingViewModel()
    {
        _trainingService = new TrainingService();
        Config.DataDir = Path.Combine(Environment.CurrentDirectory, "train_data");
        Config.OutputDir = Path.Combine(Environment.CurrentDirectory, "output");
        Config.LabelFilePath = Path.Combine(Environment.CurrentDirectory, "train_data", "det_train_label.txt");

        // 默认字典文件：优先使用本应用自带的
        var builtInDict = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ppocr_v5_models", "onnx", "ppocrv5_dict.txt");
        if (File.Exists(builtInDict))
        {
            Config.DictFilePath = builtInDict;
        }
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

    [RelayCommand]
    private void BrowsePretrainedModel()
    {
        var dialog = new OpenFolderDialog { Title = "选择预训练模型目录（包含 .pdmodel 和 .pdparams 文件）" };
        if (dialog.ShowDialog() == true)
            Config.PretrainedModelDir = dialog.FolderName;
    }

    [RelayCommand]
    private void AutoDetectPretrainedModel()
    {
        var detected = _trainingService.AutoDetectPretrainedModel(PaddleocrDir, Config.Mode);
        if (detected != null)
        {
            Config.PretrainedModelDir = detected;
            StatusMessage = $"已自动检测预训练模型: {detected}";
        }
        else
        {
            var info = TrainingService.GetPretrainedModelDownloadInfo(Config.Mode);
            MessageBox.Show(
                $"未在 PaddleOCR 目录下找到匹配的预训练模型。\n\n{info}",
                "未找到预训练模型",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private void AutoDetectDictFile()
    {
        var detected = _trainingService.AutoDetectDictFile(PaddleocrDir);
        if (detected != null)
        {
            Config.DictFilePath = detected;
            StatusMessage = $"已自动检测字典文件: {detected}";
        }
        else
        {
            // 尝试从 ONNX 模型目录查找
            var onnxDict = System.IO.Path.Combine(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ppocr_v5_models", "onnx"),
                "ppocrv5_dict.txt");
            if (System.IO.File.Exists(onnxDict))
            {
                Config.DictFilePath = onnxDict;
                StatusMessage = $"已使用本应用自带的字典: {onnxDict}";
            }
            else
            {
                MessageBox.Show(
                    "未找到字典文件。\n\n" +
                    "字典文件通常位于 PaddleOCR/ppocr/utils/ 目录下。\n" +
                    "您也可以使用本应用自带的 ppocrv5_dict.txt。",
                    "未找到字典文件",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
    }

    // ─── 一键自动配置 ─────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAutoConfigure))]
    private async Task AutoConfigure()
    {
        if (IsConfiguring) return;
        IsConfiguring = true;
        ConfigureLog = string.Empty;
        CurrentConfigureStep = "准备中...";

        try
        {
            var progress = new Progress<(string Step, string Detail, bool IsError)>(item =>
            {
                CurrentConfigureStep = $"[{item.Step}]";
                ConfigureLog += $"[{item.Step}] {item.Detail}\n";
                if (item.IsError)
                {
                    ConfigureLog += $"  ⚠ 错误\n";
                }
            });

            var (ok, log) = await _trainingService.AutoConfigureEnvironment(
                PaddleocrDir,
                Config.Mode,
                ConfigureDownloadPretrained,
                ConfigureInstallDeps,
                ConfigureInstallPaddle,
                ConfigurePaddleCpu,
                progress);

            ConfigureLog = log;

            if (ok)
            {
                // 自动填充检测到的路径
                var (det, rec, dict) = _trainingService.GetDetectedPaths(PaddleocrDir);
                if (Config.Mode == TrainingMode.Detection && det != null)
                    Config.PretrainedModelDir = det;
                if (Config.Mode == TrainingMode.Recognition && rec != null)
                    Config.PretrainedModelDir = rec;
                if (dict != null)
                    Config.DictFilePath = dict;

                CurrentConfigureStep = "✅ 配置完成";
                StatusMessage = "环境自动配置完成";
                MessageBox.Show("环境配置完成！\n\n检测到的路径已自动填入训练参数。",
                    "配置成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                CurrentConfigureStep = "❌ 配置失败";
                StatusMessage = "环境配置失败，请查看日志";
            }
        }
        catch (Exception ex)
        {
            ConfigureLog += $"\n[异常] {ex.Message}\n";
            CurrentConfigureStep = "❌ 异常";
            StatusMessage = $"配置异常: {ex.Message}";
        }
        finally
        {
            IsConfiguring = false;
        }
    }

    private bool CanAutoConfigure() => !IsConfiguring && !IsTrainingRunning;

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
