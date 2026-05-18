using System.Windows;

namespace PaddleOcrDesktop.Models;

public class TextRegion
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public Point[] Points { get; set; } = System.Array.Empty<Point>();
    public bool IsValid { get; set; }
    public string ValidationMessage { get; set; } = string.Empty;
}
