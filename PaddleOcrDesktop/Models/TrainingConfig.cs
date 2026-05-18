// Models/TrainingConfig.cs
namespace PaddleOcrDesktop.Models;

/// <summary>
/// 训练类型
/// </summary>
public enum TrainingMode
{
    /// <summary>
    /// 检测模型训练
    /// </summary>
    Detection,
    /// <summary>
    /// 识别模型训练
    /// </summary>
    Recognition
}

/// <summary>
/// 训练设备
/// </summary>
public enum TrainingDevice
{
    CPU,
    GPU
}

/// <summary>
/// 训练配置
/// </summary>
public class TrainingConfig
{
    /// <summary>
    /// 训练数据目录
    /// </summary>
    public string DataDir { get; set; } = string.Empty;

    /// <summary>
    /// 标注文件路径
    /// </summary>
    public string LabelFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 训练类型
    /// </summary>
    public TrainingMode Mode { get; set; } = TrainingMode.Detection;

    /// <summary>
    /// 训练设备
    /// </summary>
    public TrainingDevice Device { get; set; } = TrainingDevice.CPU;

    /// <summary>
    /// GPU 设备 ID
    /// </summary>
    public int GpuDeviceId { get; set; } = 0;

    /// <summary>
    /// 学习率
    /// </summary>
    public double LearningRate { get; set; } = 0.001;

    /// <summary>
    /// 训练轮数
    /// </summary>
    public int Epochs { get; set; } = 100;

    /// <summary>
    /// 批大小
    /// </summary>
    public int BatchSize { get; set; } = 8;

    /// <summary>
    /// 输出目录
    /// </summary>
    public string OutputDir { get; set; } = "./output";

    /// <summary>
    /// 预训练模型目录（微调时使用）
    /// PaddleOCR 格式：包含 .pdmodel 和 .pdparams 文件的目录
    /// 如果留空，将从头开始训练
    /// </summary>
    public string? PretrainedModelDir { get; set; }

    /// <summary>
    /// 字典文件路径（rec 模式必须）
    /// 每行一个字符，用于定义识别字符集
    /// </summary>
    public string? DictFilePath { get; set; }

    /// <summary>
    /// ONNX 模型目录（本应用导出格式）
    /// 训练前会自动查找是否需要转换
    /// </summary>
    public string? OnnxModelDir { get; set; }

    /// <summary>
    /// 训练图片最短边
    /// </summary>
    public int ImageShapeShort { get; set; } = 640;

    /// <summary>
    /// 是否使用多进程数据加载
    /// </summary>
    public bool UseMultiprocess { get; set; } = false;

    /// <summary>
    /// 工作进程数
    /// </summary>
    public int NumWorkers { get; set; } = 2;
}
