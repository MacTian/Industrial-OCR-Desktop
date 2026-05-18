namespace PaddleOcrDesktop.Models;

public class BatchTask
{
    public string ImagePath { get; set; } = string.Empty;
    public BatchTaskStatus Status { get; set; } = BatchTaskStatus.Pending;
    public OcrResult? Result { get; set; }
}

public enum BatchTaskStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}
