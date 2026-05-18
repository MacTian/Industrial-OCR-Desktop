using System.Globalization;
using System.IO;
using System.Text;
using ClosedXML.Excel;
using PaddleOcrDesktop.Models;

namespace PaddleOcrDesktop.Services;

public class ExportService
{
    /// <summary>
    /// 导出为 TXT（每行一条识别结果）
    /// </summary>
    public void ExportToTxt(string filePath, List<OcrResult> results)
    {
        var sb = new StringBuilder();
        foreach (var result in results)
        {
            sb.AppendLine($"=== {Path.GetFileName(result.ImagePath)} ===");
            sb.AppendLine($"识别耗时: {result.ElapsedMilliseconds}ms");
            sb.AppendLine($"状态: {(result.IsSuccess ? "成功" : "失败")}");
            if (!result.IsSuccess)
            {
                sb.AppendLine($"错误: {result.ErrorMessage}");
                sb.AppendLine();
                continue;
            }

            foreach (var region in result.Regions)
            {
                sb.AppendLine($"  #{region.Id}: {region.Text} (置信度: {region.Confidence:P1}, 校验: {(region.IsValid ? "通过" : "异常")})");
                if (!region.IsValid)
                    sb.AppendLine($"    └─ {region.ValidationMessage}");
            }
            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 导出为 CSV
    /// </summary>
    public void ExportToCsv(string filePath, List<OcrResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("图片文件名,区域编号,文本,置信度,校验状态,校验消息");

        foreach (var result in results)
        {
            var fileName = EscapeCsvField(Path.GetFileName(result.ImagePath));
            if (!result.IsSuccess)
            {
                sb.Append($"{fileName},0,,,失败,{EscapeCsvField(result.ErrorMessage)}");
                continue;
            }

            foreach (var region in result.Regions)
            {
                var text = EscapeCsvField(region.Text);
                var status = region.IsValid ? "通过" : "异常";
                var msg = EscapeCsvField(region.ValidationMessage);
                sb.AppendLine($"{fileName},{region.Id},{text},{region.Confidence:F4},{status},{msg}");
            }
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// 导出为 Excel
    /// </summary>
    public void ExportToExcel(string filePath, List<OcrResult> results)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("OCR 识别结果");

        // 表头
        worksheet.Cell(1, 1).Value = "图片文件名";
        worksheet.Cell(1, 2).Value = "区域编号";
        worksheet.Cell(1, 3).Value = "文本内容";
        worksheet.Cell(1, 4).Value = "置信度";
        worksheet.Cell(1, 5).Value = "校验状态";
        worksheet.Cell(1, 6).Value = "校验消息";

        var headerRange = worksheet.Range(1, 1, 1, 6);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

        int row = 2;
        foreach (var result in results)
        {
            var fileName = Path.GetFileName(result.ImagePath);
            if (!result.IsSuccess)
            {
                worksheet.Cell(row, 1).Value = fileName;
                worksheet.Cell(row, 2).Value = 0;
                worksheet.Cell(row, 5).Value = "失败";
                worksheet.Cell(row, 6).Value = result.ErrorMessage;
                row++;
                continue;
            }

            foreach (var region in result.Regions)
            {
                worksheet.Cell(row, 1).Value = fileName;
                worksheet.Cell(row, 2).Value = region.Id;
                worksheet.Cell(row, 3).Value = region.Text;
                worksheet.Cell(row, 4).Value = region.Confidence;
                worksheet.Cell(row, 4).Style.NumberFormat.Format = "0.00%";
                worksheet.Cell(row, 5).Value = region.IsValid ? "通过" : "异常";
                worksheet.Cell(row, 6).Value = region.ValidationMessage;

                // 异常行标红
                if (!region.IsValid)
                {
                    worksheet.Range(row, 1, row, 6).Style.Fill.BackgroundColor = XLColor.LightPink;
                }

                row++;
            }
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}
