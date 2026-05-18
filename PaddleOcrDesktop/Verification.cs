// 验证测试工具 - 直接测试OCR识别功能
// 在WPF应用启动时调用 VerificationTests.RunTests() 可以验证识别功能是否正常

using System;
using System.IO;
using OpenCvSharp;
using PaddleOcrDesktop.Models;
using PaddleOcrDesktop.Services;
using System.Windows;
using CvPoint = OpenCvSharp.Point;

namespace PaddleOcrDesktop;

public static class VerificationTests
{
    /// <summary>
    /// 运行OCR识别验证测试
    /// </summary>
    public static void RunTests()
    {
        Console.WriteLine("========== OCR识别验证测试 ==========");

        var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "ppocr_v5_models");

        // 测试1: 验证模型文件是否存在
        Console.WriteLine("\n[测试1] 验证模型文件");
        bool allModelsExist = VerifyModelFiles(modelPath);
        Console.WriteLine(allModelsExist ? "✓ 模型文件完整" : "✗ 模型文件缺失");

        if (!allModelsExist)
        {
            MessageBox.Show("模型文件不完整，请检查Assets/ppocr_v5_models目录", "模型验证失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        // 测试2: 验证引擎加载
        Console.WriteLine("\n[测试2] 验证OCR引擎加载");
        var ocrEngine = new OcrEngine(modelPath);
        var loadLogs = ocrEngine.LoadModel();
        Console.WriteLine($"模型加载结果: {ocrEngine.IsLoaded}");
        foreach (var log in loadLogs)
        {
            Console.WriteLine($"  {log}");
        }

        if (!ocrEngine.IsLoaded)
        {
            MessageBox.Show("OCR模型加载失败，无法进行识别测试", "加载失败", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }

        // 测试3: 创建测试图片并识别
        Console.WriteLine("\n[测试3] 创建测试图片并识别");
        string testImagePath = Path.Combine(Path.GetTempPath(), "ocr_test.png");
        CreateTestImage(testImagePath);
        Console.WriteLine($"测试图片已创建: {testImagePath}");

        if (File.Exists(testImagePath))
        {
            var result = ocrEngine.Recognize(testImagePath);
            Console.WriteLine($"识别结果: IsSuccess={result.IsSuccess}");
            Console.WriteLine($"耗时: {result.ElapsedMilliseconds}ms");

            if (result.IsSuccess)
            {
                Console.WriteLine($"识别到 {result.Regions.Count} 个区域:");
                foreach (var region in result.Regions)
                {
                    Console.WriteLine($"  [{region.Id}] 文本: {region.Text}, 置信度: {region.Confidence:P1}");
                }
            }
            else
            {
                Console.WriteLine($"错误信息: {result.ErrorMessage}");
            }

            // 测试4: 无效路径测试
            Console.WriteLine("\n[测试4] 无效路径测试");
            var errorResult = ocrEngine.Recognize("/invalid/path/test.jpg");
            Console.WriteLine($"无效路径识别结果: IsSuccess={errorResult.IsSuccess}, Error={errorResult.ErrorMessage}");

            File.Delete(testImagePath);
            Console.WriteLine($"临时文件已清理: {testImagePath}");
        }

        Console.WriteLine("\n========== 验证测试完成 ==========");

        if (allModelsExist && ocrEngine.IsLoaded)
        {
            MessageBox.Show("OCR验证测试完成！引擎加载成功，可以正常使用。", "验证结果", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("OCR验证测试未通过，请检查错误信息。", "验证结果", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 验证模型文件是否存在
    /// </summary>
    private static bool VerifyModelFiles(string modelPath)
    {
        var detFiles = Directory.Exists(Path.Combine(modelPath, "det_model"))
            ? Directory.GetFiles(Path.Combine(modelPath, "det_model"))
            : Array.Empty<string>();

        var recFiles = Directory.Exists(Path.Combine(modelPath, "rec_model"))
            ? Directory.GetFiles(Path.Combine(modelPath, "rec_model"))
            : Array.Empty<string>();

        var clsFiles = Directory.Exists(Path.Combine(modelPath, "cls_model"))
            ? Directory.GetFiles(Path.Combine(modelPath, "cls_model"))
            : Array.Empty<string>();

        bool detValid = detFiles.Length > 0 && detFiles.Any(f => f.EndsWith("inference.yml"));
        bool recValid = recFiles.Length > 0 && recFiles.Any(f => f.EndsWith("inference.yml"));
        bool clsValid = clsFiles.Length > 0;

        Console.WriteLine($"  检测模型: {(detValid ? "✓" : "✗")} ({detFiles.Length} 个文件)");
        Console.WriteLine($"  识别模型: {(recValid ? "✓" : "✗")} ({recFiles.Length} 个文件)");
        Console.WriteLine($"  分类模型: {(clsValid ? "✓" : "✗")} ({clsFiles.Length} 个文件)");

        return detValid && recValid;
    }

    /// <summary>
    /// 创建简单的测试图片
    /// </summary>
    private static void CreateTestImage(string path)
    {
        using var mat = new Mat(200, 400, MatType.CV_8UC3, Scalar.White);
        Cv2.PutText(mat, "Test Text 123456789", new CvPoint(20, 100),
            HersheyFonts.HersheySimplex, 0.6, Scalar.Black, 2);
        Cv2.ImWrite(path, mat);
    }
}
