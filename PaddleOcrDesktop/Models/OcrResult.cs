namespace PaddleOcrDesktop.Models;

public class OcrResult
{
    public string ImagePath { get; set; } = string.Empty;
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public List<TextRegion> Regions { get; set; } = new();
    public long ElapsedMilliseconds { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
