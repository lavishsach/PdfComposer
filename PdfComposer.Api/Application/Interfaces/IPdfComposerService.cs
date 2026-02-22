using PdfComposer.Api.Application.Models;
using System.Text.Json;

namespace PdfComposer.Api.Application.Interfaces;

public interface IPdfComposerService
{
    Task<Stream> GenerateFromTemplateAsync(
        string templateHtml,
        JsonElement data,
        string? templateCss = null,
        PdfRenderOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<Stream> ConvertToPdfAsync(
        Stream fileStream,
        string fileType,
        CancellationToken cancellationToken = default);

    Task<Stream> MergePdfAsync(
        List<Stream> pdfStreams,
        PdfPostProcessOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<Stream> ComposeFinalPdfAsync(
        ComposeFinalPdfRequest request,
        CancellationToken cancellationToken = default);
}
