using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;

namespace PdfComposer.Api.Infrastructure.Converters;

public sealed class TextToPdfConverter(IHtmlToPdfConverter htmlToPdfConverter) : IFileToPdfConverter
{
    private readonly IHtmlToPdfConverter _htmlToPdfConverter = htmlToPdfConverter;

    public bool CanConvert(SupportedFileType fileType) => fileType == SupportedFileType.Txt;

    public async Task<Stream> ConvertAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(input, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        var html = $"<pre style='white-space: pre-wrap; font-family: monospace; font-size: 12px;'>{System.Net.WebUtility.HtmlEncode(text)}</pre>";
        return await _htmlToPdfConverter.ConvertAsync(html, new PdfRenderOptions(), cancellationToken);
    }
}
