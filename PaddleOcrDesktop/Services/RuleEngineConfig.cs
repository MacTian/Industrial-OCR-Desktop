using System.IO;
using PaddleOcrDesktop.Models;

namespace PaddleOcrDesktop.Services;

public static class RuleEngineConfig
{
    /// <summary>
    /// 默认规则：仅允许字母和数字，长度 3~50
    /// </summary>
    public static List<ValidationRule> GetDefaultRules() => new()
    {
        new ValidationRule
        {
            Name = "字符集校验",
            AllowedChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
        },
        new ValidationRule
        {
            Name = "长度校验",
            MinLength = 1,
            MaxLength = 100
        }
    };

    /// <summary>
    /// 从 JSON 文件加载规则
    /// </summary>
    public static List<ValidationRule> LoadFromJson(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return GetDefaultRules();

        try
        {
            var json = File.ReadAllText(jsonPath);
            return System.Text.Json.JsonSerializer.Deserialize<List<ValidationRule>>(json) ?? GetDefaultRules();
        }
        catch
        {
            return GetDefaultRules();
        }
    }

    /// <summary>
    /// 保存规则到 JSON 文件
    /// </summary>
    public static void SaveToJson(string jsonPath, List<ValidationRule> rules)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(rules, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(jsonPath, json);
    }
}
