using System.IO;
using System.Threading;
using OpenCvSharp;
using PaddleOcrDesktop.Models;

namespace PaddleOcrDesktop.Services;

public class OcrService
{
    private readonly OcrEngine _ocrEngine;
    private readonly RuleEngine _ruleEngine;
    private readonly int _maxConcurrency;
    private static readonly string LogFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr_debug.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
        System.Diagnostics.Debug.WriteLine($"[OCR] {msg}");
    }

    public event EventHandler<OcrProgressEventArgs>? ProgressChanged;

    public OcrEngine OcrEngine => _ocrEngine;

    public OcrService(OcrEngine ocrEngine, RuleEngine ruleEngine, int maxConcurrency = 1)
    {
        _ocrEngine = ocrEngine;
        _ruleEngine = ruleEngine;
        _maxConcurrency = maxConcurrency;
    }

    /// <summary>
    /// 暴露 OcrEngine 的 LoadModel 方法
    /// </summary>
    public List<string> LoadModel()
    {
        return _ocrEngine.LoadModel();
    }

    /// <summary>
    /// 单张图片识别
    /// </summary>
    public async Task<OcrResult> RecognizeSingleAsync(string imagePath, Rect? roi = null)
    {
        return await Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = _ocrEngine.Recognize(imagePath, roi);
            sw.Stop();
            Log($"OcrEngine.Recognize: {sw.ElapsedMilliseconds}ms, 区域: {result.Regions.Count}, 图片: {result.ImageWidth}x{result.ImageHeight}");

            if (result.IsSuccess)
            {
                var ruleSw = System.Diagnostics.Stopwatch.StartNew();
                _ruleEngine.ValidateAll(result.Regions);
                ruleSw.Stop();
                Log($"规则校验: {ruleSw.ElapsedMilliseconds}ms");
            }
            return result;
        });
    }

    /// <summary>
    /// 批量识别
    /// </summary>
    public async Task<List<OcrResult>> RecognizeBatchAsync(
        List<string> imagePaths,
        CancellationToken cancellationToken = default)
    {
        var results = new OcrResult[imagePaths.Count];
        using var semaphore = new SemaphoreSlim(_maxConcurrency);
        var completedCount = 0;
        var totalCount = imagePaths.Count;

        var tasks = imagePaths.Select(async (path, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = _ocrEngine.Recognize(path);
                if (result.IsSuccess)
                {
                    _ruleEngine.ValidateAll(result.Regions);
                }

                results[index] = result;
                var count = Interlocked.Increment(ref completedCount);
                ProgressChanged?.Invoke(this, new OcrProgressEventArgs
                {
                    Current = count,
                    Total = totalCount,
                    CurrentFile = Path.GetFileName(path),
                    Status = $"已完成 {count}/{totalCount}"
                });
            }
            catch (OperationCanceledException)
            {
                results[index] = new OcrResult
                {
                    ImagePath = path,
                    IsSuccess = false,
                    ErrorMessage = "已取消"
                };
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
        return results.ToList();
    }
}

public class OcrProgressEventArgs : EventArgs
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
