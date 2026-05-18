# OCR识别验证测试指南

## 验证目的

直接测试OCR引擎是否能正确加载模型并返回识别结果，解决"点击识别按钮没有结果"的问题。

## 方法1：使用独立测试程序（推荐）

项目路径：`OcrTest/`

### 编译和运行（Windows上）

1. 打开命令提示符：
```cmd
cd C:\Users\mtian\source\repos\PaddleOcrDesktop\OcrTest
```

2. 还原依赖：
```cmd
dotnet restore
```

3. 编译：
```cmd
dotnet build
```

4. 运行测试（使用默认测试图片）：
```cmd
dotnet run
```

5. 或者使用自己的图片进行测试：
```cmd
dotnet run "C:\path\to\your\image.jpg"
```

### 预期输出

成功情况：
```
========== OCR识别直接验证 ==========

模型路径: C:\...\Assets\ppocr_v5_models
检测模型目录: ... -> 存在
识别模型目录: ... -> 存在

测试图片已创建: C:\Users\...\ocr_test.png

=== 识别测试 ===
耗时: 1234ms
区域数: 3
  [1] 测试文本 (置信度: 95.2%)
  [2] Test Text (置信度: 88.5%)
  [3] 123456789 (置信度: 92.1%)
✓ 识别成功完成！
```

如果模型未找到：
```
✗ 识别失败: DirectoryNotFoundException: 检测模型目录不存在: ...
```
这种情况下需要确认模型文件是否正确放置在 `Assets/ppocr_v5_models/` 目录下。

## 方法2：在WPF应用运行时验证

在 `App.xaml.cs` 的 `Application_Startup` 中调用验证：

```csharp
private void Application_Startup(object sender, StartupEventArgs e)
{
    // 仅在调试模式下运行验证测试
#if DEBUG
    VerificationTests.RunTests();
#endif
}
```

## 方法3：单元测试

可使用 `NUnit` 或 `xUnit` 编写更正式的单元测试，测试代码示例：

```csharp
[TestFixture]
public class OcrEngineTests
{
    [Test]
    public void Recognize_ValidImage_ReturnsResult()
    {
        var engine = new OcrEngine(modelPath);
        engine.LoadModel();
        
        var result = engine.Recognize("test.jpg");
        
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Regions, Is.Not.Empty);
    }
}
```

## 常见失败原因

1. **模型文件缺失**：
   - 确认 `Assets/ppocr_v5_models/` 目录下包含三个子目录：`det_model/`, `rec_model/`, `cls_model/`
   - 每个子目录需包含 `inference.yml` 和模型参数文件

2. **路径权限问题**：
   - 确保应用有权限读取模型文件和图片文件
   - Windows上注意路径分隔符问题

3. **运行时环境**：
   - 需要安装 VC++ 运行时或 OpenCvSharp 的 native runtime
   - 确保 `OpenCvSharp4.runtime.win` 和 `Sdcb.PaddleInference.runtime.win64.openblas` 正确部署

## 验证步骤清单

- [ ] 运行 OcrTest 程序，使用任何包含中文/英文字符的图片
- [ ] 观察输出中"识别成功完成"的标记
- [ ] 确认控制台显示了识别到的文本和置信度
- [ ] 若失败，查看错误信息中的具体原因
- [ ] 检查模型文件完整性
- [ ] 确认输出目录下有 `Assets/ppocr_v5_models/` 

## 问题排查

**Q: 提示“无法读取图片”**
A: 图片路径可能包含不支持的字符或路径太长，尝试用简单路径如 `C:\test\img.jpg`

**Q: 程序崩溃，无错误信息**
A: 可能是缺少 native runtime，查看事件查看器中的详细错误

**Q: 耗时非常长（>10秒）**
A: 第一次运行时需要加载模型到显存，后续识别会更快

**Q: 区域数为0**
A: 可能测试图片没有有效文字，或文字太小/模糊，尝试使用清晰的文本图片
