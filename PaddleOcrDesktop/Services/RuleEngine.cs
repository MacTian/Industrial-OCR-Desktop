using System.Text.RegularExpressions;
using PaddleOcrDesktop.Models;

namespace PaddleOcrDesktop.Services;

public class RuleEngine
{
    private readonly List<ValidationRule> _rules = new();

    public void LoadRules(IEnumerable<ValidationRule> rules)
    {
        _rules.Clear();
        _rules.AddRange(rules);
    }

    public void AddRule(ValidationRule rule) => _rules.Add(rule);

    public void ClearRules() => _rules.Clear();

    /// <summary>
    /// 校验单个文本区域，所有规则 AND 逻辑
    /// </summary>
    public (bool IsValid, string Message) Validate(TextRegion region)
    {
        if (_rules.Count == 0)
            return (true, string.Empty);

        foreach (var rule in _rules)
        {
            var (valid, message) = ValidateSingleRule(region.Text, rule);
            if (!valid)
                return (false, message);
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// 批量校验并直接修改 TextRegion 的 IsValid 和 ValidationMessage
    /// </summary>
    public void ValidateAll(List<TextRegion> regions)
    {
        foreach (var region in regions)
        {
            var (isValid, message) = Validate(region);
            region.IsValid = isValid;
            region.ValidationMessage = message;
        }
    }

    private (bool IsValid, string Message) ValidateSingleRule(string text, ValidationRule rule)
    {
        if (rule.MinLength.HasValue && text.Length < rule.MinLength.Value)
            return (false, $"[{rule.Name}] 长度不足，最少 {rule.MinLength} 个字符");

        if (rule.MaxLength.HasValue && text.Length > rule.MaxLength.Value)
            return (false, $"[{rule.Name}] 长度超出，最多 {rule.MaxLength} 个字符");

        if (!string.IsNullOrEmpty(rule.AllowedChars))
        {
            foreach (char c in text)
            {
                if (!rule.AllowedChars.Contains(c))
                    return (false, $"[{rule.Name}] 包含非法字符 '{c}'，允许字符集: {rule.AllowedChars}");
            }
        }

        if (!string.IsNullOrEmpty(rule.RegexPattern))
        {
            if (!Regex.IsMatch(text, rule.RegexPattern))
                return (false, $"[{rule.Name}] 格式不匹配正则: {rule.RegexPattern}");
        }

        return (true, string.Empty);
    }
}
