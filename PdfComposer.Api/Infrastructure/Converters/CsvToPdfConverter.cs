using CsvHelper;
using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using System.Globalization;
using System.Text;

namespace PdfComposer.Api.Infrastructure.Converters;

public sealed class CsvToPdfConverter(IHtmlToPdfConverter htmlToPdfConverter) : IFileToPdfConverter
{
    private readonly IHtmlToPdfConverter _htmlToPdfConverter = htmlToPdfConverter;

    public bool CanConvert(SupportedFileType fileType) => fileType == SupportedFileType.Csv;

    public async Task<Stream> ConvertAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
    {
        if (input.CanSeek)
        {
            input.Position = 0;
        }

        using var reader = new StreamReader(input, leaveOpen: true);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("<table style='width:100%; border-collapse: collapse; font-size: 11px;'>");

        var rowIndex = 0;
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = csv.Parser.Record;
            if (record is null || record.Length == 0)
            {
                continue;
            }

            sb.Append("<tr>");
            foreach (var field in record)
            {
                var tag = rowIndex == 0 ? "th" : "td";
                sb.Append($"<{tag} style='border:1px solid #cccccc; padding:6px; text-align:left;'>");
                sb.Append(System.Net.WebUtility.HtmlEncode(field));
                sb.Append($"</{tag}>");
            }
            sb.Append("</tr>");
            rowIndex++;
        }

        sb.Append("</table>");
        return await _htmlToPdfConverter.ConvertAsync(sb.ToString(), new PdfRenderOptions(), cancellationToken);
    }
}
