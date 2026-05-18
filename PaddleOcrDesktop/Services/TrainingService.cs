// Services/TrainingService.cs
using System.Diagnostics;
using System.Formats.Tar;
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

        // 3. 检查预训练模型（可选，仅提示）
        var pretrainedDet = AutoDetectPretrainedModel(paddleocrDir, TrainingMode.Detection);
        var pretrainedRec = AutoDetectPretrainedModel(paddleocrDir, TrainingMode.Recognition);
        if (pretrainedDet != null)
            messages.Add($"✓ 预训练检测模型: {pretrainedDet}");
        else
            messages.Add("○ 预训练检测模型: 未找到（将从零训练）");
        if (pretrainedRec != null)
            messages.Add($"✓ 预训练识别模型: {pretrainedRec}");
        else
            messages.Add("○ 预训练识别模型: 未找到（将从零训练）");

        // 4. 检查字典文件（rec 模式需要）
        var dictFile = AutoDetectDictFile(paddleocrDir);
        if (dictFile != null)
            messages.Add($"✓ 字典文件: {dictFile}");
        else
            messages.Add("○ 字典文件: 未找到（rec 模式必须手动指定）");

        // 5. 检查 paddlepaddle 包
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
    /// 自动检测预训练模型路径
    /// 在 PaddleOCR 目录下查找常见的预训练模型位置
    /// </summary>
    public string? AutoDetectPretrainedModel(string paddleocrDir, TrainingMode mode)
    {
        // PaddleOCR 预训练模型常见位置
        var searchDirs = new[]
        {
            Path.Combine(paddleocrDir, "inference"),
            Path.Combine(paddleocrDir, "models"),
            Path.Combine(paddleocrDir, "output", "best_accuracy"),
            Path.Combine(paddleocrDir, "output"),
        };

        var prefix = mode == TrainingMode.Detection ? "det" : "rec";

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir)) continue;

            // 查找 .pdmodel 文件
            var pdfiles = Directory.GetFiles(dir, "*.pdmodel", SearchOption.AllDirectories);
            foreach (var pdf in pdfiles)
            {
                var name = Path.GetFileNameWithoutExtension(pdf).ToLowerInvariant();
                // 匹配 det_*.pdmodel 或 rec_*.pdmodel
                if (name.StartsWith(prefix + "_") || name.Contains(prefix))
                {
                    return Path.GetDirectoryName(pdf);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 自动检测字典文件路径
    /// </summary>
    public string? AutoDetectDictFile(string paddleocrDir)
    {
        var searchPaths = new[]
        {
            Path.Combine(paddleocrDir, "ppocr", "utils", "ppocr_keys_v1.txt"),
            Path.Combine(paddleocrDir, "ppocr", "utils", "dict", "ppocr_keys_v1.txt"),
            Path.Combine(paddleocrDir, "configs", "rec", "PP-OCRv3", "rec_multi_language_lite_train.yml"),
        };

        // 先查找 txt 字典文件
        var dictFiles = Directory.GetFiles(Path.Combine(paddleocrDir, "ppocr"), "*.txt", SearchOption.AllDirectories);
        foreach (var f in dictFiles)
        {
            var name = Path.GetFileName(f).ToLowerInvariant();
            if (name.Contains("key") || name.Contains("dict"))
            {
                return f;
            }
        }

        foreach (var path in searchPaths)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    /// <summary>
    /// 获取推荐的预训练模型下载信息
    /// </summary>
    public static string GetPretrainedModelDownloadInfo(TrainingMode mode)
    {
        if (mode == TrainingMode.Detection)
        {
            return "检测模型推荐下载：\n" +
                   "  PP-OCRv5 中文检测模型\n" +
                   "  https://paddleocr.bj.bcebos.com/PP-OCRv5/chinese/ch_PP-OCRv5_det_infer.tar\n\n" +
                   "解压后将 .pdmodel 和 .pdparams 文件放入 PaddleOCR/inference/ 目录";
        }
        else
        {
            return "识别模型推荐下载：\n" +
                   "  PP-OCRv5 中文识别模型\n" +
                   "  https://paddleocr.bj.bcebos.com/PP-OCRv5/chinese/ch_PP-OCRv5_rec_infer.tar\n\n" +
                   "解压后将 .pdmodel 和 .pdparams 文件放入 PaddleOCR/inference/ 目录";
        }
    }

    /// <summary>
    /// 一键自动配置训练环境
    /// </summary>
    public async Task<(bool Ok, string Log)> AutoConfigureEnvironment(
        string paddleocrDir,
        TrainingMode mode,
        bool downloadPretrained,
        bool installDeps,
        bool installPaddle,
        bool paddleCpu,
        IProgress<(string Step, string Detail, bool IsError)>? progress = null)
    {
        var log = new System.Text.StringBuilder();
        string? pythonExe = null;
        var isWindows = OperatingSystem.IsWindows();

        void Report(string step, string detail, bool isError = false)
        {
            progress?.Report((step, detail, isError));
            log.AppendLine($"[{step}] {detail}");
        }

        // ── 辅助：启动进程（Windows 上用 cmd /c 确保 PATH 搜索）──
        ProcessStartInfo CreateCmd(string fileName, string arguments, string? workingDir = null)
        {
            // Windows 上 git/python 可能不在直接 PATH 中，用 cmd /c 确保能找到
            if (isWindows && !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                // 已经是完整路径的直接用，否则用 cmd /c
                if (!Path.IsPathRooted(fileName) && !File.Exists(fileName))
                {
                    return new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {fileName} {arguments}",
                        WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                        StandardErrorEncoding = System.Text.Encoding.UTF8
                    };
                }
            }
            return new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
        }

        // ── 辅助：解压 tar 文件（跨平台）──
        async Task<bool> ExtractTarAsync(string tarPath, string destDir)
        {
            try
            {
                // 优先使用系统 tar 命令
                if (isWindows)
                {
                    try
                    {
                        var psi = CreateCmd("tar.exe", $"-xf \"{tarPath}\" -C \"{destDir}\"");
                        using var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            await proc.WaitForExitAsync();
                            if (proc.ExitCode == 0) return true;
                        }
                    }
                    catch { /* tar.exe 不可用，继续回退 */ }
                }
                else
                {
                    try
                    {
                        var psi = CreateCmd("tar", $"-xf \"{tarPath}\" -C \"{destDir}\"");
                        using var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            await proc.WaitForExitAsync();
                            if (proc.ExitCode == 0) return true;
                        }
                    }
                    catch { /* tar 不可用，继续回退 */ }
                }

                // 回退：使用 .NET 内置 System.Formats.Tar（.NET 7+）
                Report("解压", "使用 .NET 内置 TarReader 解压...");
                return await ExtractTarManagedAsync(tarPath, destDir);
            }
            catch (Exception ex)
            {
                Report("解压", $"解压失败: {ex.Message}", true);
            }
            return false;
        }

        // ── Step 1: 检查/克隆 PaddleOCR ──
        Report("1/6", "检查 PaddleOCR 目录...");
        if (string.IsNullOrEmpty(paddleocrDir) || !Directory.Exists(paddleocrDir))
        {
            Report("1/6", "PaddleOCR 目录不存在，尝试自动克隆...");
            try
            {
                var parentDir = Path.GetDirectoryName(paddleocrDir);
                if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
                {
                    Report("1/6", $"父目录不存在: {parentDir}", true);
                    return (false, log.ToString());
                }

                var psi = CreateCmd("git", $"clone https://github.com/PaddlePaddle/PaddleOCR.git \"{paddleocrDir}\"", parentDir);
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    Report("1/6", "无法启动 git 进程。请确认 Git 已安装并加入 PATH。", true);
                    Report("1/6", "下载地址: https://git-scm.com/downloads", true);
                    return (false, log.ToString());
                }
                await proc.WaitForExitAsync();
                if (proc.ExitCode != 0)
                {
                    var err = await proc.StandardError.ReadToEndAsync();
                    Report("1/6", $"git clone 失败: {err}", true);
                    return (false, log.ToString());
                }
                Report("1/6", $"✓ PaddleOCR 已克隆到: {paddleocrDir}");
            }
            catch (Exception ex)
            {
                Report("1/6", $"克隆失败: {ex.Message}", true);
                if (ex is System.ComponentModel.Win32Exception)
                {
                    Report("1/6", "提示: 请确认 Git 已安装并加入 PATH", true);
                }
                return (false, log.ToString());
            }
        }
        else
        {
            Report("1/6", $"✓ PaddleOCR 目录已存在: {paddleocrDir}");
        }

        var trainPyPath = Path.Combine(paddleocrDir, "tools", "train.py");
        if (!File.Exists(trainPyPath))
        {
            Report("1/6", "未找到 tools/train.py，仓库可能不完整", true);
            return (false, log.ToString());
        }

        // ── Step 2: 检查 Python ──
        Report("2/6", "检查 Python 环境...");
        foreach (var exe in new[] { "python", "python3" })
        {
            try
            {
                var psi = CreateCmd(exe, "--version");
                using var proc = Process.Start(psi);
                if (proc == null) continue;
                await proc.WaitForExitAsync();
                var ver = (await proc.StandardOutput.ReadToEndAsync()).Trim();
                if (string.IsNullOrEmpty(ver))
                    ver = (await proc.StandardError.ReadToEndAsync()).Trim();
                if (!string.IsNullOrEmpty(ver))
                {
                    pythonExe = exe;
                    Report("2/6", $"✓ {ver} ({exe})");
                    break;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 命令不存在，继续尝试下一个
            }
            catch { }
        }
        if (pythonExe == null)
        {
            Report("2/6", "✗ 未找到 Python，请先安装 Python 3.8+", true);
            Report("2/6", "下载地址: https://www.python.org/downloads/", true);
            Report("2/6", "安装时请勾选 'Add Python to PATH'", true);
            return (false, log.ToString());
        }

        // ── Step 3: 安装 PaddlePaddle ──
        if (installPaddle)
        {
            Report("3/6", "检查 PaddlePaddle...");
            try
            {
                var psi = CreateCmd(pythonExe, "-c \"import paddle; print(paddle.__version__)\"");
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    await proc.WaitForExitAsync();
                    var ver = (await proc.StandardOutput.ReadToEndAsync()).Trim();
                    if (!string.IsNullOrEmpty(ver))
                    {
                        Report("3/6", $"✓ PaddlePaddle {ver} 已安装");
                    }
                    else
                    {
                        var pkg = paddleCpu ? "paddlepaddle" : "paddlepaddle-gpu";
                        var label = paddleCpu ? "CPU" : "GPU";
                        Report("3/6", $"安装 PaddlePaddle ({label} 版本)...");
                        Report("3/6", $"这可能需要几分钟，请耐心等待...");
                        var pipPsi = CreateCmd(pythonExe, $"-m pip install {pkg} -i https://mirrors.aliyun.com/pypi/simple/");
                        using var pipProc = Process.Start(pipPsi);
                        if (pipProc != null)
                        {
                            var outputTask = Task.Run(async () =>
                            {
                                while (!pipProc.StandardOutput.EndOfStream)
                                {
                                    var line = await pipProc.StandardOutput.ReadLineAsync();
                                    if (!string.IsNullOrWhiteSpace(line))
                                        Report("3/6", $"  {line}");
                                }
                            });
                            await pipProc.WaitForExitAsync();
                            await outputTask;
                            Report("3/6", pipProc.ExitCode == 0
                                ? $"✓ PaddlePaddle ({label}) 安装成功"
                                : $"✗ 安装失败 (exit {pipProc.ExitCode})", pipProc.ExitCode != 0);
                        }
                        else
                        {
                            Report("3/6", "无法启动 pip 安装进程", true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Report("3/6", $"检查/安装失败: {ex.Message}", true);
            }
        }
        else
        {
            Report("3/6", "跳过（未勾选）");
        }

        // ── Step 4: 安装 PaddleOCR 依赖 ──
        if (installDeps)
        {
            Report("4/6", "安装 PaddleOCR Python 依赖...");
            var reqFile = Path.Combine(paddleocrDir, "requirements.txt");
            if (File.Exists(reqFile))
            {
                try
                {
                    var pipPsi = CreateCmd(pythonExe, $"-m pip install -r \"{reqFile}\" -i https://mirrors.aliyun.com/pypi/simple/", paddleocrDir);
                    using var pipProc = Process.Start(pipPsi);
                    if (pipProc != null)
                    {
                        var outputTask = Task.Run(async () =>
                        {
                            while (!pipProc.StandardOutput.EndOfStream)
                            {
                                var line = await pipProc.StandardOutput.ReadLineAsync();
                                if (line != null)
                                    Report("4/6", $"  {line}");
                            }
                        });
                        await pipProc.WaitForExitAsync();
                        await outputTask;
                        Report("4/6", pipProc.ExitCode == 0
                            ? "✓ 依赖安装完成"
                            : $"✗ 部分依赖安装失败 (exit {pipProc.ExitCode})", pipProc.ExitCode != 0);
                    }
                    else
                    {
                        Report("4/6", "无法启动 pip 安装进程", true);
                    }
                }
                catch (Exception ex)
                {
                    Report("4/6", $"安装失败: {ex.Message}", true);
                }
            }
            else
            {
                Report("4/6", "未找到 requirements.txt，跳过");
            }
        }
        else
        {
            Report("4/6", "跳过（未勾选）");
        }

        // ── Step 5: 下载预训练模型 ──
        if (downloadPretrained)
        {
            Report("5/6", "检查预训练模型...");
            var inferenceDir = Path.Combine(paddleocrDir, "inference");
            Directory.CreateDirectory(inferenceDir);

            var detModelDir = AutoDetectPretrainedModel(paddleocrDir, TrainingMode.Detection);
            var recModelDir = AutoDetectPretrainedModel(paddleocrDir, TrainingMode.Recognition);

            // 下载检测模型
            if (detModelDir == null && mode == TrainingMode.Detection)
            {
                var url = "https://paddleocr.bj.bcebos.com/PP-OCRv5/chinese/ch_PP-OCRv5_det_infer.tar";
                var tarPath = Path.Combine(inferenceDir, "det_model.tar");
                try
                {
                    Report("5/6", "下载检测预训练模型...");
                    Report("5/6", $"  URL: {url}");
                    using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(15) };
                    var response = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    await using var fs = new FileStream(tarPath, FileMode.Create, FileAccess.Write);
                    await using var stream = await response.Content.ReadAsStreamAsync();
                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read));
                        totalRead += read;
                        if (totalBytes > 0)
                        {
                            var pct = (int)(totalRead * 100 / totalBytes);
                            Report("5/6", $"  下载中... {pct}% ({totalRead / 1024 / 1024}MB / {totalBytes / 1024 / 1024}MB)");
                        }
                    }
                    Report("5/6", $"✓ 下载完成 ({totalRead / 1024 / 1024}MB)");

                    Report("5/6", "解压检测模型...");
                    var ok = await ExtractTarAsync(tarPath, inferenceDir);
                    Report("5/6", ok ? "✓ 检测模型解压完成" : "✗ 解压失败", !ok);
                    File.Delete(tarPath);
                }
                catch (Exception ex)
                {
                    Report("5/6", $"下载/解压失败: {ex.Message}", true);
                }
            }
            else if (mode == TrainingMode.Detection)
            {
                Report("5/6", $"✓ 检测模型已存在: {detModelDir}");
            }

            // 下载识别模型
            if (recModelDir == null && mode == TrainingMode.Recognition)
            {
                var url = "https://paddleocr.bj.bcebos.com/PP-OCRv5/chinese/ch_PP-OCRv5_rec_infer.tar";
                var tarPath = Path.Combine(inferenceDir, "rec_model.tar");
                try
                {
                    Report("5/6", "下载识别预训练模型...");
                    Report("5/6", $"  URL: {url}");
                    using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(15) };
                    var response = await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    await using var fs = new FileStream(tarPath, FileMode.Create, FileAccess.Write);
                    await using var stream = await response.Content.ReadAsStreamAsync();
                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read));
                        totalRead += read;
                        if (totalBytes > 0)
                        {
                            var pct = (int)(totalRead * 100 / totalBytes);
                            Report("5/6", $"  下载中... {pct}% ({totalRead / 1024 / 1024}MB / {totalBytes / 1024 / 1024}MB)");
                        }
                    }
                    Report("5/6", $"✓ 下载完成 ({totalRead / 1024 / 1024}MB)");

                    Report("5/6", "解压识别模型...");
                    var ok = await ExtractTarAsync(tarPath, inferenceDir);
                    Report("5/6", ok ? "✓ 识别模型解压完成" : "✗ 解压失败", !ok);
                    File.Delete(tarPath);
                }
                catch (Exception ex)
                {
                    Report("5/6", $"下载/解压失败: {ex.Message}", true);
                }
            }
            else if (mode == TrainingMode.Recognition)
            {
                Report("5/6", $"✓ 识别模型已存在: {recModelDir}");
            }
        }
        else
        {
            Report("5/6", "跳过（未勾选）");
        }

        // ── Step 6: 自动检测并设置参数 ──
        Report("6/6", "自动检测配置参数...");
        var finalDet = AutoDetectPretrainedModel(paddleocrDir, TrainingMode.Detection);
        var finalRec = AutoDetectPretrainedModel(paddleocrDir, TrainingMode.Recognition);
        var finalDict = AutoDetectDictFile(paddleocrDir);

        if (finalDet != null) Report("6/6", $"✓ 检测模型: {finalDet}");
        if (finalRec != null) Report("6/6", $"✓ 识别模型: {finalRec}");
        if (finalDict != null) Report("6/6", $"✓ 字典文件: {finalDict}");

        Report("完成", "环境配置完成！");

        return (true, log.ToString());
    }

    /// <summary>
    /// 使用 .NET 内置 TarReader 解压 tar 文件（.NET 7+ 回退方案）
    /// </summary>
    private async Task<bool> ExtractTarManagedAsync(string tarPath, string destDir)
    {
        try
        {
            await using var fs = File.OpenRead(tarPath);
            using var reader = new System.Formats.Tar.TarReader(fs);
            Directory.CreateDirectory(destDir);
            int count = 0;
            TarEntry? entry;
            while ((entry = reader.GetNextEntry(copyData: false)) != null)
            {
                var entryName = entry.Name.Replace('/', Path.DirectorySeparatorChar);
                var destPath = Path.Combine(destDir, entryName);

                if (entry.EntryType == TarEntryType.Directory)
                {
                    Directory.CreateDirectory(destPath);
                }
                else
                {
                    var dir = Path.GetDirectoryName(destPath);
                    if (dir != null) Directory.CreateDirectory(dir);
                    // Use DataStream to read file content
                    await using var entryStream = entry.DataStream ?? throw new InvalidOperationException($"Tar entry '{entry.Name}' has no data stream.");
                    await using var outStream = File.Create(destPath);
                    await entryStream.CopyToAsync(outStream);
                    count++;
                }
            }
            return count > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ExtractTarManagedAsync failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取自动配置结果中的检测到的路径
    /// </summary>
    public (string? DetModel, string? RecModel, string? DictFile) GetDetectedPaths(string paddleocrDir)
    {
        return (
            AutoDetectPretrainedModel(paddleocrDir, TrainingMode.Detection),
            AutoDetectPretrainedModel(paddleocrDir, TrainingMode.Recognition),
            AutoDetectDictFile(paddleocrDir)
        );
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

        var isWindows = OperatingSystem.IsWindows();
        ProcessStartInfo psi;

        if (isWindows)
        {
            // Windows 上用 cmd /c 确保 python 能被 PATH 找到
            psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c python -u \"{trainPy}\" -c \"{configPath}\" {gpuArgs}",
                WorkingDirectory = paddleocrDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
        }
        else
        {
            psi = new ProcessStartInfo
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
        }

        try
        {
            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.Start();

            // 将错误输出合并到标准输出流
            proc.ErrorDataReceived += (_, e) =>
            {
                // 错误数据会通过 StandardError 读取，不合并
            };

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            return proc;
        }
        catch
        {
            return null;
        }
    }
}
