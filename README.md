# Industrial OCR Desktop（工业文字识别系统）

基于 [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) 的 .NET WPF 桌面 OCR 应用，通过 [RapidOcrNet](https://github.com/RapidAI/RapidOcrNet) 调用 PP-OCRv5 ONNX 模型。支持图片文字识别、ROI 区域选择、数据标注和训练数据导出。

A .NET WPF desktop OCR application built on PaddleOCR via RapidOcrNet with PP-OCRv5 ONNX models. Supports image text recognition, ROI selection, annotation, and training data export for model fine-tuning.

---

## 功能特性 / Features

- **OCR 文字识别 / OCR Recognition** — 基于 PP-OCRv5 模型的全图/ROI 区域文字检测与识别
- **ROI 区域选择 / ROI Selection** — 在图片上框选感兴趣区域，只识别选中部分
- **批量处理 / Batch Processing** — 支持整个文件夹的批量识别，带进度显示
- **标注模式 / Annotation Mode** — 手动绘制多边形标注框，编辑文本内容，管理标签
- **OCR 预标注 / OCR Pre-annotation** — 自动用 OCR 生成标注结果，支持人工修改和删除
- **训练数据导出 / Training Data Export** — 导出 PaddleOCR 标准格式的训练数据（检测 + 识别），用于模型微调
- **结果导出 / Result Export** — 支持导出识别结果为 Excel (.xlsx)、CSV 或纯文本

---

## 运行环境 / Requirements

- .NET 8.0 SDK
- Windows 10/11 (x64)
- 模型文件约 21MB / ~21MB for ONNX models

---

## 快速开始 / Quick Start

### 1. 克隆并编译 / Clone and Build

```bash
git clone https://github.com/MacTian/Industrial-OCR-Desktop.git
cd Industrial-OCR-Desktop
dotnet build PaddleOcrDesktop/PaddleOcrDesktop.csproj
```

### 2. 下载模型 / Download Models

下载 PP-OCRv5 ONNX 模型，放置到 `PaddleOcrDesktop/Assets/ppocr_v5_models/onnx/` 目录下：

Download PP-OCRv5 ONNX models and place them under `PaddleOcrDesktop/Assets/ppocr_v5_models/onnx/`:

| 文件 / File | 说明 / Description | 大小 / Size |
|------|------|------|
| `ch_PP-OCRv5_det_infer.onnx` | 文字检测模型 / Text detection | ~4.6 MB |
| `ch_PP-OCRv5_rec_infer.onnx` | 文字识别模型 / Text recognition | ~16 MB |
| `ch_ppocr_mobile_v2.0_cls_infer.onnx` | 方向分类模型 / Classification | ~571 KB |
| `ppocrv5_dict.txt` | 字符字典 / Character dict | ~60 KB |

模型可从 [PaddleOCR 模型列表](https://github.com/PaddlePaddle/PaddleOCR/blob/release/2.8/doc/doc_en/models_list_en.md) 下载。

### 3. 运行 / Run

```bash
dotnet run --project PaddleOcrDesktop/PaddleOcrDesktop.csproj
```

---

## 使用说明 / Usage

### 识别模式 / Recognition Mode

1. 点击 **📂 打开图片** 或 **📁 打开文件夹** 加载图片 / Open images or a folder
2. 可选：在图片上框选 ROI 区域 / Optionally draw an ROI on the image
3. 点击 **🔍 识别** 执行 OCR / Run OCR
4. 识别结果在右侧面板显示，可通过 **📤 导出** 导出 / View and export results

### 标注模式 / Annotation Mode

1. 打开图片或文件夹 / Open images or a folder
2. 使用 **◀ 上一张 / ▶ 下一张** 或点击右侧图片列表切换图片 / Switch images via buttons or image list
3. 开启 **✏️ 绘制标注**，在图片上绘制多边形标注框 / Toggle drawing mode and draw polygons:
   - 左键单击添加顶点 / Left-click to add vertices
   - 双击完成绘制，输入标注文本 / Double-click to finish and enter text
   - 右键取消当前绘制 / Right-click to cancel
4. 点击 **🔍 OCR预标注** 自动生成标注，然后人工审核修改 / Auto-generate annotations via OCR, then review and modify
5. 在右侧标注列表的 DataGrid 中直接编辑文本，勾选/取消"忽略"标记无效区域 / Edit text inline in the DataGrid, toggle "忽略" for invalid regions
6. 选中标注后按 **Delete** 键或点击 **🗑️ 删除选中** 删除标注 / Press Delete or click 🗑️ to remove selected annotation
7. **💾 保存标注** 将项目保存为 JSON，**📤 导出训练数据** 导出 PaddleOCR 格式 / Save as JSON or export in PaddleOCR training format

### 训练数据格式 / Training Data Format

导出的数据遵循 PaddleOCR 标准目录结构：

```
output_dir/
├── images/              # 原始图片 / Original images
├── det_train_label.txt  # 检测标签（JSON 格式）/ Detection labels
├── rec_images/
│   └── crop_img/        # 裁剪后的标注区域图片 / Cropped region images
└── rec_train_label.txt  # 识别标签 / Recognition labels
```

---

## 项目结构 / Project Structure

```
PaddleOcrDesktop/
├── Models/              # 数据模型 / Data models
├── ViewModels/          # MVVM 视图模型 / ViewModels
├── Views/               # WPF XAML 视图 / Views
├── Services/            # 业务逻辑 / Business logic
└── Assets/              # ONNX 模型文件 / ONNX model files
```

---

## 技术栈 / Tech Stack

- **.NET 8.0** + WPF
- **CommunityToolkit.Mvvm** — MVVM 源生成器 / MVVM source generators
- **RapidOcrNet 2.0.0** — 纯 .NET ONNX Runtime OCR 库
- **OpenCvSharp4** — 图像处理（ROI、裁剪）/ Image processing
- **SkiaSharp** — 图片尺寸读取 / Image dimension reading
- **ClosedXML** — Excel 导出 / Excel export

---

## 许可证 / License

MIT
