// Services/TrainingService.cs
using System.Diagnostics;
using System.IO;
using PaddleOcrDesktop.Models;

namespace PaddleOcrDesktop.Services;

public class TrainingService
{
    /// <summary>
    /// 检查训练环境是否就绪
    /// </summary>
    public (bool Ok, string Message) CheckEnvironment(string paddleocrDir)
    {
        if (!Directory.Exists(paddleocrDir))
            return (false, $"PaddleOCR 目录不存在: {paddleocrDir}");

        var trainPy = Path.Combine(paddleocrDir, "tools", "train.py");
        if (!File.Exists(trainPy))
            return (false, $"未找到训练脚本: {trainPy}");

        // 检查 Python 环境
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            var version = proc?.StandardOutput.ReadToEnd().Trim();
            if (string.IsNullOrEmpty(version))
                return (false, "未检测到 Python 环境，请确保 Python 已安装并加入 PATH");

            return (true, $"环境检查通过。Python: {version}\nPaddleOCR: {paddleocrDir}");
        }
        catch (Exception ex)
        {
            return (false, $"检查 Python 环境失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 生成训练配置 YAML 内容
    /// </summary>
    public string GenerateTrainingConfigYaml(TrainingConfig config)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# 自动生成的训练配置");
        sb.AppendLine($"# 生成时间: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("Global:");
        sb.AppendLine($"  output_dir: {config.OutputDir}");
        sb.AppendLine($"  save_epoch_step: 1");
        sb.AppendLine($"  eval_batch_step: [0, 500]");
        sb.AppendLine($"  cal_metric_during_train: true");
        sb.AppendLine($"  pretrained_model: {config.PretrainedModelDir ?? ""}");
        sb.AppendLine();
        sb.AppendLine("Architecture:");
        sb.AppendLine($"  model_type: {(config.Mode == TrainingMode.Detection ? "det" : "rec")}");
        sb.AppendLine("  algorithm: DB");
        sb.AppendLine();
        sb.AppendLine("Train:");
        sb.AppendLine($"  dataset:");
        sb.AppendLine($"    name: SimpleDataSet");
        sb.AppendLine($"    data_dir: {config.DataDir}");
        sb.AppendLine($"    label_file_list: [\"{config.LabelFilePath}\"]");
        sb.AppendLine($"    ratio_list: [1.0]");
        sb.AppendLine($"    transforms:");
        sb.AppendLine($"      - DecodeImage: {{img_mode: BGR, channel_first: false}}");
        sb.AppendLine($"  loader:");
        sb.AppendLine($"    shuffle: true");
        sb.AppendLine($"    batch_size_per_card: {config.BatchSize}");
        sb.AppendLine($"    drop_last: true");
        sb.AppendLine($"    num_workers: {config.NumWorkers}");
        sb.AppendLine();
        sb.AppendLine("Optimizer:");
        sb.AppendLine($"  name: Adam");
        sb.AppendLine($"  lr:");
        sb.AppendLine($"    name: Cosine");
        sb.AppendLine($"    learning_rate: {config.LearningRate}");
        sb.AppendLine();
        sb.AppendLine("PostProcess:");
        sb.AppendLine("  name: DBPostProcess");

        if (config.Mode == TrainingMode.Recognition)
        {
            sb.AppendLine();
            sb.AppendLine("Loss:");
            sb.AppendLine("  name: CTCLoss");
            sb.AppendLine();
            sb.AppendLine("Metric:");
            sb.AppendLine("  name: RecMetric");
            sb.AppendLine("  main_indicator: acc");

            if (!string.IsNullOrEmpty(config.DictFilePath))
            {
                sb.AppendLine();
                sb.AppendLine("CharacterDict:");
                sb.AppendLine($"  dict_path: {config.DictFilePath}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 启动训练进程
    /// </summary>
    public Process? StartTraining(TrainingConfig config, string configPath, string paddleocrDir)
    {
        var trainPy = Path.Combine(paddleocrDir, "tools", "train.py");
        if (!File.Exists(trainPy))
            return null;

        var gpuArgs = config.Device == TrainingDevice.GPU
            ? $"--gpus {config.GpuDeviceId}"
            : "--use_gpu false";

        var psi = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"-u {trainPy} -c {configPath} {gpuArgs}",
            WorkingDirectory = paddleocrDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        try
        {
            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Start();

            // 将错误输出重定向到标准输出
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    proc.BeginOutputReadLine();
            };

            proc.BeginOutputReadLine();
            return proc;
        }
        catch
        {
            return null;
        }
    }
}
