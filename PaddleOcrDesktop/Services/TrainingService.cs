// Services/TrainingService.cs
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using PaddleOcrDesktop.Models;

namespace PaddleOcrDesktop.Services;

public class TrainingService
{
    // ── Windows 常见安装路径 ──
    private static readonly string[] GitCommonPaths = new[]
    {
        @"C:\Program Files\Git\cmd\git.exe",
        @"C:\Program Files (x86)\Git\cmd\git.exe",
        @"C:\Git\cmd\git.exe",
    };

    private static readonly string[] PythonCommonPaths = new[]
    {
        @"C:\Python312\python.exe",
        @"C:\Python311\python.exe",
        @"C:\Python310\python.exe",
        @"C:\Python39\python.exe",
        @"C:\Python38\python.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312", "python.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python311", "python.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python310", "python.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python39", "python.exe"),
    };

    // ── 解析可执行文件路径（Windows 上尝试常见安装位置）──
    private static string ResolveExe(string cmd, string[] commonPaths)
    {
        // 如果 cmd 已经是 .exe 结尾且存在，直接返回
        if (cmd.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(cmd))
            return cmd;

        // 尝试常见路径
        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
                return path;
        }

        return cmd; // 回退原始命令
    }

    // ── 辅助：通过 shell 执行命令，返回 stdout+stderr 合并输出 ──
    // 用 cmd.exe(Windows) 或 bash(Linux/Mac) 包装，自动搜索 PATH
    private static string RunShellCommand(string command, int timeoutMs = 30000)
    {
        var isWin = OperatingSystem.IsWindows();

        // Windows 上解析 git/python 常见安装路径
        if (isWin)
        {
            var firstSpace = command.IndexOf(' ');
            var exeName = firstSpace > 0 ? command.Substring(0, firstSpace) : command;
            var args = firstSpace > 0 ? command.Substring(firstSpace) : "";

            if (exeName.Equals("git", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ResolveExe(exeName, GitCommonPaths);
                if (resolved != exeName) command = resolved + args;
            }
            else if (exeName.Equals("python", StringComparison.OrdinalIgnoreCase) || exeName.Equals("python3", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ResolveExe(exeName, PythonCommonPaths);
                if (resolved != exeName) command = resolved + args;
            }
        }

        var tempOut = Path.GetTempFileName();
        var tempErr = Path.GetTempFileName();
        try
        {
            if (isWin)
            {
                var batFile = Path.GetTempFileName() + ".bat";
                // 如果命令路径包含空格（如 C:\Program Files\...），需要加引号
                var quotedCommand = command;
                var firstSpace = command.IndexOf(' ');
                if (firstSpace > 0)
                {
                    var exe = command.Substring(0, firstSpace);
                    var rest = command.Substring(firstSpace);
                    if (exe.Contains(' ') && !exe.StartsWith("\""))
                        quotedCommand = $"\"{exe}\"{rest}";
                }
                File.WriteAllText(batFile, $"@echo off\r\n{quotedCommand} > \"{tempOut}\" 2> \"{tempErr}\"\r\n");
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batFile}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return "";
                proc.WaitForExit(timeoutMs);
                try { File.Delete(batFile); } catch { }
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"{command}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return "";
                proc.WaitForExit(timeoutMs);
                var output = proc.StandardOutput.ReadToEnd();
                var error = proc.StandardError.ReadToEnd();
                return (output + "\n" + error).Trim();
            }

            var stdout = File.Exists(tempOut) ? File.ReadAllText(tempOut).Trim() : "";
            var stderr = File.Exists(tempErr) ? File.ReadAllText(tempErr).Trim() : "";
            return (stdout + "\n" + stderr).Trim();
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            try { File.Delete(tempErr); } catch { }
        }
    }

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
        // 用 cmd /c 包装让系统搜索 PATH，避免 Win32Exception
        string? pythonExe = null;
        string? pythonVersion = null;

        foreach (var exe in new[] { "python", "python3" })
        {
            try
            {
                var version = RunShellCommand($"{exe} --version");
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
        if (pythonExe != null)
        {
            try
            {
                var paddleVersion = RunShellCommand($"{pythonExe} -c \"import paddle; print(paddle.__version__)\"");
                if (!string.IsNullOrEmpty(paddleVersion))
                {
                    messages.Add($"✓ PaddlePaddle: {paddleVersion}");
                }
                else
                {
                    messages.Add("✗ PaddlePaddle: 未安装");
                    return (false,
                        string.Join("\n", messages) + "\n\n" +
                        "未检测到 PaddlePaddle 包。\n" +
                        "请安装: pip install paddlepaddle\n" +
                        "(GPU 版本: pip install paddlepaddle-gpu)");
                }
            }
            catch
            {
                messages.Add("? PaddlePaddle: 检查失败");
            }
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
        System.Diagnostics.Debug.WriteLine($"========== AutoConfigureEnvironment START ==========");
        System.Diagnostics.Debug.WriteLine($"paddleocrDir={paddleocrDir}");
        System.Diagnostics.Debug.WriteLine($"mode={mode}, downloadPretrained={downloadPretrained}, installDeps={installDeps}, installPaddle={installPaddle}, paddleCpu={paddleCpu}");
        System.Diagnostics.Debug.WriteLine($"OS={Environment.OSVersion}, IsWindows={OperatingSystem.IsWindows()}");

        var log = new System.Text.StringBuilder();
        string? pythonExe = null;
        var isWindows = OperatingSystem.IsWindows();

        void Report(string step, string detail, bool isError = false)
        {
            System.Diagnostics.Debug.WriteLine($"[Report] {step}: {detail} (error={isError})");
            progress?.Report((step, detail, isError));
            log.AppendLine($"[{step}] {detail}");
        }

        // ── 辅助：运行命令并返回 (exitCode, stdout, stderr) ──
        // 使用 cmd.exe /c 包装让系统搜索 PATH，输出重定向到临时文件
        async Task<(int Code, string Out, string Err)> RunAsync(string cmd, string args, string? workDir = null, int timeoutMs = 600000)
        {
            System.Diagnostics.Debug.WriteLine($"[RunAsync] cmd={cmd}, args={args}, workDir={workDir}");

            // Windows 上尝试解析常见安装路径
            if (isWindows)
            {
                var resolvedCmd = cmd;
                if (cmd.Equals("git", StringComparison.OrdinalIgnoreCase))
                    resolvedCmd = ResolveExe(cmd, GitCommonPaths);
                else if (cmd.Equals("python", StringComparison.OrdinalIgnoreCase) || cmd.Equals("python3", StringComparison.OrdinalIgnoreCase))
                    resolvedCmd = ResolveExe(cmd, PythonCommonPaths);

                if (resolvedCmd != cmd)
                {
                    System.Diagnostics.Debug.WriteLine($"[RunAsync] resolved '{cmd}' -> '{resolvedCmd}'");
                    cmd = resolvedCmd;
                }
            }

            var tempOut = Path.GetTempFileName();
            var tempErr = Path.GetTempFileName();
            try
            {
                if (isWindows)
                {
                    var batFile = Path.GetTempFileName() + ".bat";
                    var batContent = $"@echo off\r\n\"{cmd}\" {args} > \"{tempOut}\" 2> \"{tempErr}\"\r\n";
                    await File.WriteAllTextAsync(batFile, batContent);
                    System.Diagnostics.Debug.WriteLine($"[RunAsync] batFile={batFile}");
                    System.Diagnostics.Debug.WriteLine($"[RunAsync] batContent={batContent.Trim()}");

                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{batFile}\"",
                        WorkingDirectory = workDir ?? Environment.CurrentDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[RunAsync] Process.Start returned null for {cmd}");
                        return (-1, "", $"无法启动: {cmd}");
                    }

                    await proc.WaitForExitAsync();
                    System.Diagnostics.Debug.WriteLine($"[RunAsync] exitCode={proc.ExitCode}");
                    try { File.Delete(batFile); } catch { }

                    var output = File.Exists(tempOut) ? await File.ReadAllTextAsync(tempOut) : "";
                    var error = File.Exists(tempErr) ? await File.ReadAllTextAsync(tempErr) : "";
                    System.Diagnostics.Debug.WriteLine($"[RunAsync] output={output.Trim()}, error={error.Trim()}");
                    return (proc.ExitCode, output, error);
                }
                else
                {
                    // Linux/Mac: 用 bash -c，命令 + 重定向放在同一字符串中
                    var fullCmd = $"{cmd} {args}";
                    var psi = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"-c \"{fullCmd}\"",
                        WorkingDirectory = workDir ?? Environment.CurrentDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc == null)
                        return (-1, "", $"无法启动: {cmd}");

                    var outputTask = proc.StandardOutput.ReadToEndAsync();
                    var errorTask = proc.StandardError.ReadToEndAsync();
                    await proc.WaitForExitAsync();
                    var output = await outputTask;
                    var error = await errorTask;
                    return (proc.ExitCode, output, error);
                }
            }
            finally
            {
                try { File.Delete(tempOut); } catch { }
                try { File.Delete(tempErr); } catch { }
            }
        }

        // ── 辅助：解压 tar 文件（跨平台）──
        async Task<bool> ExtractTarAsync(string tarPath, string destDir)
        {
            try
            {
                var tarExe = isWindows ? "tar.exe" : "tar";
                var (code, _, err) = await RunAsync(tarExe, $"-xf \"{tarPath}\" -C \"{destDir}\"", destDir);
                if (code == 0) return true;
                Report("解压", $"系统 tar 失败: {err}", true);
            }
            catch { }

            // 回退：使用 .NET 内置 System.Formats.Tar
            Report("解压", "使用 .NET 内置 TarReader 解压...");
            return await ExtractTarManagedAsync(tarPath, destDir);
        }

        // ── Step 1: 检查/克隆 PaddleOCR ──
        Report("1/6", "检查 PaddleOCR 目录...");
        if (string.IsNullOrEmpty(paddleocrDir) || !Directory.Exists(paddleocrDir))
        {
            Report("1/6", "PaddleOCR 目录不存在，尝试自动克隆...");
            var parentDir = Path.GetDirectoryName(paddleocrDir);
            if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
            {
                Report("1/6", $"父目录不存在: {parentDir}", true);
                return (false, log.ToString());
            }

            try
            {
                var (code, output, err) = await RunAsync("git", $"clone https://github.com/PaddlePaddle/PaddleOCR.git \"{paddleocrDir}\"", parentDir);
                if (code != 0)
                {
                    Report("1/6", $"git clone 失败 (exit {code}): {err}", true);
                    if (err.Contains("not found") || err.Contains("不是内部或外部命令"))
                        Report("1/6", "Git 未安装或未加入 PATH。下载地址: https://git-scm.com/downloads", true);
                    return (false, log.ToString());
                }
                Report("1/6", $"✓ PaddleOCR 已克隆到: {paddleocrDir}");
            }
            catch (Exception ex)
            {
                Report("1/6", $"克隆异常: {ex.GetType().Name}: {ex.Message}", true);
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
                var (code, output, err) = await RunAsync(exe, "--version");
                var ver = (output + err).Trim(); // python --version 输出到 stdout 或 stderr
                if (code == 0 && !string.IsNullOrEmpty(ver))
                {
                    pythonExe = exe;
                    Report("2/6", $"✓ {ver} ({exe})");
                    break;
                }
                Report("2/6", $"尝试 {exe}: exit={code}, output='{output}', err='{err}'");
            }
            catch (Exception ex)
            {
                Report("2/6", $"尝试 {exe} 异常: {ex.GetType().Name}: {ex.Message}");
            }
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
                var (code, output, _) = await RunAsync(pythonExe, "-c \"import paddle; print(paddle.__version__)\"", timeoutMs: 30000);
                var ver = output.Trim();
                if (code == 0 && !string.IsNullOrEmpty(ver))
                {
                    Report("3/6", $"✓ PaddlePaddle {ver} 已安装");
                }
                else
                {
                    var pkg = paddleCpu ? "paddlepaddle" : "paddlepaddle-gpu";
                    var label = paddleCpu ? "CPU" : "GPU";
                    Report("3/6", $"安装 PaddlePaddle ({label} 版本)...");
                    Report("3/6", "这可能需要几分钟，请耐心等待...");
                    var (pipCode, pipOut, pipErr) = await RunAsync(pythonExe, $"-m pip install {pkg} -i https://mirrors.aliyun.com/pypi/simple/", timeoutMs: 600000);
                    // 输出末尾几行日志
                    var lines = pipOut.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(5);
                    foreach (var l in lines) Report("3/6", $"  {l.Trim()}");
                    Report("3/6", pipCode == 0
                        ? $"✓ PaddlePaddle ({label}) 安装成功"
                        : $"✗ 安装失败 (exit {pipCode}): {pipErr}", pipCode != 0);
                }
            }
            catch (Exception ex)
            {
                Report("3/6", $"检查/安装异常: {ex.GetType().Name}: {ex.Message}", true);
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
                    var (code, pipOut, pipErr) = await RunAsync(pythonExe, $"-m pip install -r \"{reqFile}\" -i https://mirrors.aliyun.com/pypi/simple/", paddleocrDir, 600000);
                    var lines = pipOut.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(5);
                    foreach (var l in lines) Report("4/6", $"  {l.Trim()}");
                    Report("4/6", code == 0
                        ? "✓ 依赖安装完成"
                        : $"✗ 部分依赖安装失败 (exit {code}): {pipErr}", code != 0);
                }
                catch (Exception ex)
                {
                    Report("4/6", $"安装异常: {ex.GetType().Name}: {ex.Message}", true);
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

        var isWin = OperatingSystem.IsWindows();

        // Windows 上解析 python 路径
        var pythonCmd = "python";
        if (isWin)
        {
            var resolved = ResolveExe("python", PythonCommonPaths);
            if (resolved != "python") pythonCmd = resolved;
        }

        ProcessStartInfo psi;

        if (isWin)
        {
            // Windows: 用 cmd /c 包装让系统搜索 PATH，同时保持输出重定向
            psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {pythonCmd} -u \"{trainPy}\" -c \"{configPath}\" {gpuArgs}",
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
