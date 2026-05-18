namespace PaddleOcrDesktop.Models;

public class ValidationRule
{
    public string Name { get; set; } = string.Empty;
    public string RegexPattern { get; set; } = string.Empty;
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public string AllowedChars { get; set; } = string.Empty;
}
