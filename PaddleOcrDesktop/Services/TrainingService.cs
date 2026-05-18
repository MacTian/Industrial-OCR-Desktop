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
        var messages = new List<string>();

        // 1. 检查 PaddleOCR 目录
        if (string.IsNullOrEmpty(paddleocrDir) || !Directory.Exists(paddleocrDir))
        {
            return (false,
                $"PaddleOCR 目录不存在: {paddleocrDir}\n\n" +
                "请先克隆 PaddleOCR 仓库：\n" +
                "  git clone https://github.com/PaddlePaddle/PaddleOCR.git\n\n" +
                "然后在「训练模式」设置中浏览选择该目录。");
        }

        var trainPy = Path.Combine(paddleocrDir, "tools", "train.py");
        if (!File.Exists(trainPy))
        {
            return (false,
                $"未找到训练脚本: {trainPy}\n\n" +
                "请确保 PaddleOCR 仓库完整（包含 tools/train.py）。\n" +
                "如果目录正确但文件缺失，尝试：\n" +
                "  git pull && git submodule update --init");
        }

        messages.Add($"✓ PaddleOCR: {paddleocrDir}");
        messages.Add($"✓ 训练脚本: tools/train.py");

        // 2. 检查 Python 环境（尝试 python 和 python3）
        string? pythonExe = null;
        string? pythonVersion = null;

        foreach (var exe in new[] { "python", "python3" })
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) continue;
                proc.WaitForExit(5000);
                var version = proc.StandardOutput.ReadToEnd().Trim();
                if (string.IsNullOrEmpty(version))
                    version = proc.StandardError.ReadToEnd().Trim(); // Python 2 prints to stderr
                if (!string.IsNullOrEmpty(version))
                {
                    pythonExe = exe;
                    pythonVersion = version;
                    break;
                }
            }
            catch { }
        }

        if (pythonExe == null)
        {
            messages.Add("✗ Python: 未找到");
            return (false,
                string.Join("\n", messages) + "\n\n" +
                "未检测到 Python 环境。\n" +
                "请安装 Python 3.8+ 并确保 python 或 python3 命令可用。\n" +
                "下载地址: https://www.python.org/downloads/");
        }

        messages.Add($"✓ {pythonVersion} ({pythonExe})");

        // 3. 检查 paddlepaddle 包
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = "-c \"import paddle; print(paddle.__version__)\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(10000);
                var paddleVersion = proc.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(paddleVersion))
                {
                    messages.Add($"✓ PaddlePaddle: {paddleVersion}");
                }
                else
                {
                    var err = proc.StandardError.ReadToEnd().Trim();
                    messages.Add("✗ PaddlePaddle: 未安装");
                    return (false,
                        string.Join("\n", messages) + "\n\n" +
                        "未检测到 PaddlePaddle 包。\n" +
                        "请安装: pip install paddlepaddle\n" +
                        "(GPU 版本: pip install paddlepaddle-gpu)");
                }
            }
        }
        catch
        {
            messages.Add("? PaddlePaddle: 检查失败");
        }

        return (true, string.Join("\n", messages));
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
