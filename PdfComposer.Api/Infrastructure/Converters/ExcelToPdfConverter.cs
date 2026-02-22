using ClosedXML.Excel;
using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using System.Text;

namespace PdfComposer.Api.Infrastructure.Converters;

public sealed class ExcelToPdfConverter(IHtmlToPdfConverter htmlToPdfConverter) : IFileToPdfConverter
{
    private readonly IHtmlToPdfConverter _htmlToPdfConverter = htmlToPdfConverter;

    public bool CanConvert(SupportedFileType fileType)
        => fileType is SupportedFileType.Xls or SupportedFileType.Xlsx;

    public async Task<Stream> ConvertAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
    {
        if (input.CanSeek)
        {
            input.Position = 0;
        }

        using var workbook = new XLWorkbook(input);
        var sb = new StringBuilder();

        foreach (var ws in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            sb.Append($"<h3>{System.Net.WebUtility.HtmlEncode(ws.Name)}</h3>");
            sb.Append("<table style='width:100%; border-collapse: collapse; font-size: 11px;'>");

            var range = ws.RangeUsed();
            if (range is null)
            {
                sb.Append("</table>");
                continue;
            }

            foreach (var row in range.Rows())
            {
                sb.Append("<tr>");
                foreach (var cell in row.Cells())
                {
                    sb.Append("<td style='border:1px solid #cccccc; padding:6px; text-align:left;'>");
                    sb.Append(System.Net.WebUtility.HtmlEncode(cell.GetFormattedString()));
                    sb.Append("</td>");
                }
                sb.Append("</tr>");
            }

            sb.Append("</table><br />");
        }

        return await _htmlToPdfConverter.ConvertAsync(sb.ToString(), new PdfRenderOptions(), cancellationToken);
    }
}
