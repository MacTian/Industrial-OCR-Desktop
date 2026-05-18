using System.Diagnostics;
using System.IO;
using OpenCvSharp;
using PaddleOcrDesktop.Models;
using RapidOcrNet;
using SkiaSharp;

namespace PaddleOcrDesktop.Services;

public sealed class OcrEngine : IDisposable
{
    private RapidOcr? _ocr;
    private bool _isLoaded;
    private readonly string _modelPath;

    public bool IsLoaded => _isLoaded;

    public OcrEngine(string modelPath)
    {
        _modelPath = modelPath;
    }

    public List<string> LoadModel()
    {
        var logs = new List<string>();
        var onnxDir = Path.Combine(_modelPath, "onnx");

        var detPath = Path.Combine(onnxDir, "ch_PP-OCRv5_mobile_det.onnx");
        var recPath = Path.Combine(onnxDir, "ch_PP-OCRv5_rec_mobile.onnx");
        var clsPath = Path.Combine(onnxDir, "ch_ppocr_mobile_v2.0_cls_infer.onnx");
        var keysPath = Path.Combine(onnxDir, "ppocrv5_dict.txt");

        logs.Add("检查 ONNX 模型文件:");
        foreach (var f in new[] { detPath, recPath, clsPath, keysPath })
        {
            var fi = new FileInfo(f);
            logs.Add(fi.Exists ? $"  ✓ {Path.GetFileName(f)} ({fi.Length / 1024}KB)" : $"  ✗ {Path.GetFileName(f)} (缺失!)");
        }

        logs.Add("初始化 ONNX 推理引擎...");
        var sw = Stopwatch.StartNew();

        _ocr = new RapidOcr();
        _ocr.InitModels(
            detPath: detPath,
            clsPath: clsPath,
            recPath: recPath,
            keysPath: keysPath);

        sw.Stop();
        logs.Add($"✓ 引擎初始化完成 ({sw.ElapsedMilliseconds}ms)");

        logs.Add("预热推理引擎...");
        try
        {
            var warmupPath = Path.Combine(Path.GetTempPath(), "ocr_warmup.png");
            using (var bmp = new SKBitmap(640, 480))
            {
                using var canvas = new SKCanvas(bmp);
                canvas.Clear(SKColors.Gray);
                using var fs = File.Create(warmupPath);
                bmp.Encode(fs, SKEncodedImageFormat.Png, 100);
            }
            var warmupSw = Stopwatch.StartNew();
            _ocr.Detect(warmupPath, RapidOcrOptions.Default);
            warmupSw.Stop();
            logs.Add($"✓ 预热完成 ({warmupSw.ElapsedMilliseconds}ms)");
            File.Delete(warmupPath);
        }
        catch (Exception ex)
        {
            logs.Add($"⚠ 预热失败: {ex.Message}");
        }

        _isLoaded = true;
        return logs;
    }

    public Models.OcrResult Recognize(string imagePath, Rect? roi = null)
    {
        var result = new Models.OcrResult { ImagePath = imagePath };

        if (!_isLoaded || _ocr == null)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "OCR 模型未加载";
            return result;
        }

        try
        {
            using var bmp = SKBitmap.Decode(imagePath);
            if (bmp.IsEmpty)
            {
                result.IsSuccess = false;
                result.ErrorMessage = "无法读取图片";
                return result;
            }

            result.ImageWidth = bmp.Width;
            result.ImageHeight = bmp.Height;

            string processPath;
            double scaleFactor = 1.0;

            if (roi.HasValue)
            {
                var r = roi.Value;
                // Clamp ROI to image bounds
                int x = Math.Max(0, r.X);
                int y = Math.Max(0, r.Y);
                int right = Math.Min(bmp.Width, r.X + r.Width);
                int bottom = Math.Min(bmp.Height, r.Y + r.Height);
                int w = right - x;
                int h = bottom - y;
                if (w <= 0 || h <= 0)
                {
                    result.IsSuccess = false;
                    result.ErrorMessage = "ROI 区域无效";
                    return result;
                }

                // Extract ROI to a new bitmap
                using var roiBmp = new SKBitmap(w, h);
                using (var canvas = new SKCanvas(roiBmp))
                {
                    canvas.DrawBitmap(bmp,
                        new SKRectI(0, 0, w, h),
                        new SKRectI(x, y, x + w, y + h));
                }

                // Resize if too large
                const int maxDim = 1920;
                if (roiBmp.Width > maxDim || roiBmp.Height > maxDim)
                {
                    scaleFactor = (double)maxDim / Math.Max(roiBmp.Width, roiBmp.Height);
                    var newWidth = (int)(roiBmp.Width * scaleFactor);
                    var newHeight = (int)(roiBmp.Height * scaleFactor);
                    using var resized = roiBmp.Resize(new SKSizeI(newWidth, newHeight), SKFilterQuality.High);
                    processPath = Path.GetTempFileName() + ".png";
                    using var fs = File.Create(processPath);
                    resized.Encode(fs, SKEncodedImageFormat.Png, 100);
                }
                else
                {
                    processPath = Path.GetTempFileName() + ".png";
                    using var fs = File.Create(processPath);
                    roiBmp.Encode(fs, SKEncodedImageFormat.Png, 100);
                }
            }
            else
            {
                // Resize full image if too large
                const int maxDim = 1920;
                if (bmp.Width > maxDim || bmp.Height > maxDim)
                {
                    scaleFactor = (double)maxDim / Math.Max(bmp.Width, bmp.Height);
                    var newWidth = (int)(bmp.Width * scaleFactor);
                    var newHeight = (int)(bmp.Height * scaleFactor);
                    using var resized = bmp.Resize(new SKSizeI(newWidth, newHeight), SKFilterQuality.High);
                    processPath = Path.GetTempFileName() + ".png";
                    using var fs = File.Create(processPath);
                    resized.Encode(fs, SKEncodedImageFormat.Png, 100);
                }
                else
                {
                    processPath = imagePath;
                }
            }

            var sw = Stopwatch.StartNew();
            var ocrResult = _ocr.Detect(processPath, RapidOcrOptions.Default);
            sw.Stop();

            result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
            result.IsSuccess = true;

            double invScale = 1.0 / scaleFactor;
            int id = 1;
            foreach (var block in ocrResult.TextBlocks)
            {
                if (block.BoxPoints == null || block.BoxPoints.Length < 3) continue;

                var points = block.BoxPoints.Select(p => new System.Windows.Point(
                    p.X * invScale,
                    p.Y * invScale
                )).ToArray();

                result.Regions.Add(new TextRegion
                {
                    Id = id++,
                    Text = block.Text ?? "",
                    Confidence = block.BoxScore,
                    Points = points,
                    IsValid = true
                });
            }

            // Clean up temp file if we created one
            if (processPath != imagePath && File.Exists(processPath))
                File.Delete(processPath);

            return result;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = $"识别失败: {ex.Message}";
            return result;
        }
    }

    public void Dispose()
    {
        _ocr?.Dispose();
        _isLoaded = false;
    }
}
