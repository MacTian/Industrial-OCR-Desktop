// Services/AnnotationExportService.cs
using System.IO;
using System.Text;
using System.Text.Json;
using PaddleOcrDesktop.Models;
using SkiaSharp;

namespace PaddleOcrDesktop.Services;

public class AnnotationExportService
{
    /// <summary>
    /// 导出检测训练数据
    /// 格式：每行 image_path\t[{"points":[[x,y],...],"transcription":"text"},...]
    /// </summary>
    public void ExportDetTrainingData(
        string labelFilePath,
        List<AnnotationImage> annotations,
        string outputImageDir)
    {
        var sb = new StringBuilder();

        foreach (var img in annotations)
        {
            if (img.Regions.Count == 0) continue;

            // 复制图片到输出目录
            var destPath = Path.Combine(outputImageDir, Path.GetFileName(img.ImagePath));
            if (!File.Exists(destPath))
            {
                File.Copy(img.ImagePath, destPath);
            }

            // 生成标注行
            var regions = img.Regions.Select(r => new
            {
                points = r.GetPaddlePoints(),
                transcription = r.GetPaddleTranscription()
            }).ToList();

            var json = JsonSerializer.Serialize(regions, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var relativePath = Path.GetFileName(img.ImagePath);
            sb.AppendLine($"{relativePath}\t{json}");
        }

        File.WriteAllText(labelFilePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 导出识别训练数据
    /// 为每张图的每个标注区域裁剪图片，生成 crop_img/ 目录和 rec_label.txt
    /// 格式：crop_img/img_001_00001.jpg\ttext
    /// </summary>
    public void ExportRecTrainingData(
        string labelFilePath,
        string outputImageDir,
        List<AnnotationImage> annotations)
    {
        var cropDir = Path.Combine(outputImageDir, "crop_img");
        Directory.CreateDirectory(cropDir);

        var sb = new StringBuilder();
        int globalIdx = 0;

        foreach (var img in annotations)
        {
            using var bmp = SKBitmap.Decode(img.ImagePath);
            if (bmp.IsEmpty) continue;

            foreach (var region in img.Regions)
            {
                if (region.IsIgnored || region.Points.Count < 3) continue;

                // 计算 bounding box
                var minX = (int)region.Points.Min(p => p.X);
                var minY = (int)region.Points.Min(p => p.Y);
                var maxX = (int)region.Points.Max(p => p.X);
                var maxY = (int)region.Points.Max(p => p.Y);

                // Clamp to image bounds
                minX = Math.Max(0, minX);
                minY = Math.Max(0, minY);
                maxX = Math.Min(bmp.Width, maxX);
                maxY = Math.Min(bmp.Height, maxY);

                var w = maxX - minX;
                var h = maxY - minY;
                if (w <= 0 || h <= 0) continue;

                // 裁剪
                using var cropBmp = new SKBitmap(w, h);
                using (var canvas = new SKCanvas(cropBmp))
                {
                    canvas.DrawBitmap(bmp,
                        new SKRectI(0, 0, w, h),
                        new SKRectI(minX, minY, maxX, maxY));
                }

                // 保存裁剪图片
                globalIdx++;
                var cropFileName = $"{Path.GetFileNameWithoutExtension(img.ImagePath)}_{globalIdx:D5}.jpg";
                var cropPath = Path.Combine(cropDir, cropFileName);

                using (var data = cropBmp.Encode(SKEncodedImageFormat.Jpeg, 90))
                using (var fs = File.Create(cropPath))
                {
                    data.SaveTo(fs);
                }

                // 标签行
                var relativeCropPath = $"crop_img/{cropFileName}";
                sb.AppendLine($"{relativeCropPath}\t{region.Text}");
            }
        }

        File.WriteAllText(labelFilePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 保存标注项目（JSON 格式，用于标注工具内部保存/加载）
    /// </summary>
    public void SaveAnnotationProject(string filePath, List<AnnotationImage> annotations)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(annotations, options);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    /// <summary>
    /// 加载标注项目
    /// </summary>
    public List<AnnotationImage> LoadAnnotationProject(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<AnnotationImage>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<AnnotationImage>();
    }

    /// <summary>
    /// 创建 PaddleOCR 训练目录结构
    /// </summary>
    public void CreateTrainingDirectoryStructure(string baseDir, TrainingMode mode)
    {
        if (mode == TrainingMode.Detection)
        {
            Directory.CreateDirectory(Path.Combine(baseDir, "train_data"));
            Directory.CreateDirectory(Path.Combine(baseDir, "eval_data"));
        }
        else
        {
            Directory.CreateDirectory(Path.Combine(baseDir, "train_data", "rec"));
            Directory.CreateDirectory(Path.Combine(baseDir, "eval_data", "rec"));
            Directory.CreateDirectory(Path.Combine(baseDir, "train_data", "rec", "crop_img"));
            Directory.CreateDirectory(Path.Combine(baseDir, "eval_data", "rec", "crop_img"));
        }
    }
}
