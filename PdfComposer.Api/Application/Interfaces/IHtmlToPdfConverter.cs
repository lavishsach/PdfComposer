using PdfComposer.Api.Application.Models;

namespace PdfComposer.Api.Application.Interfaces;

public interface IHtmlToPdfConverter
{
    Task<Stream> ConvertAsync(string html, PdfRenderOptions options, CancellationToken cancellationToken = default);
}
