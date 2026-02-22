using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using System.Text;
using System.Text.Json;

namespace PdfComposer.Api.Infrastructure.Services;

public sealed class PdfComposerService(
    IHtmlTemplateEngine templateEngine,
    IHtmlToPdfConverter htmlToPdfConverter,
    IPdfMergeService pdfMergeService,
    IEnumerable<IFileToPdfConverter> fileConverters,
    IFileTypeResolver fileTypeResolver,
    ILogger<PdfComposerService> logger) : IPdfComposerService
{
    private readonly IHtmlTemplateEngine _templateEngine = templateEngine;
    private readonly IHtmlToPdfConverter _htmlToPdfConverter = htmlToPdfConverter;
    private readonly IPdfMergeService _pdfMergeService = pdfMergeService;
    private readonly IReadOnlyList<IFileToPdfConverter> _fileConverters = fileConverters.ToList();
    private readonly IFileTypeResolver _fileTypeResolver = fileTypeResolver;
    private readonly ILogger<PdfComposerService> _logger = logger;

    public async Task<Stream> GenerateFromTemplateAsync(
        string templateHtml,
        JsonElement data,
        string? templateCss = null,
        PdfRenderOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PdfRenderOptions();

        var renderedBody = await _templateEngine.RenderAsync(templateHtml, data, cancellationToken);
        var fullHtml = BuildFullHtml(renderedBody, templateCss, options.CustomFontCss);
        return await _htmlToPdfConverter.ConvertAsync(fullHtml, options, cancellationToken);
    }

    public async Task<Stream> ConvertToPdfAsync(Stream fileStream, string fileType, CancellationToken cancellationToken = default)
    {
        var resolvedType = _fileTypeResolver.ResolveFromString(fileType);
        if (resolvedType == SupportedFileType.Unknown)
        {
            throw new NotSupportedException($"Unsupported file type: {fileType}");
        }

        var converter = _fileConverters.FirstOrDefault(c => c.CanConvert(resolvedType));
        if (converter is null)
        {
            throw new NotSupportedException($"No converter registered for file type {resolvedType}.");
        }

        return await converter.ConvertAsync(fileStream, $"attachment.{resolvedType.ToString().ToLowerInvariant()}", cancellationToken);
    }

    public Task<Stream> MergePdfAsync(List<Stream> pdfStreams, PdfPostProcessOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new PdfPostProcessOptions();
        return _pdfMergeService.MergeAsync(pdfStreams, options, cancellationToken);
    }

    public async Task<Stream> ComposeFinalPdfAsync(ComposeFinalPdfRequest request, CancellationToken cancellationToken = default)
    {
        var transientStreams = new List<Stream>();

        try
        {
            var mainPdf = await GenerateFromTemplateAsync(
                request.TemplateHtml,
                request.Data,
                request.TemplateCss,
                request.RenderOptions,
                cancellationToken);

            transientStreams.Add(mainPdf);

            foreach (var attachment in request.Attachments)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileType = attachment.DeclaredFileType
                    ?? _fileTypeResolver.ResolveFromExtension(attachment.FileName);

                if (fileType == SupportedFileType.Unknown)
                {
                    throw new NotSupportedException($"Could not infer file type for {attachment.FileName}");
                }

                var converter = _fileConverters.FirstOrDefault(c => c.CanConvert(fileType));
                if (converter is null)
                {
                    throw new NotSupportedException($"No converter registered for {fileType}");
                }

                var converted = await converter.ConvertAsync(attachment.Content, attachment.FileName, cancellationToken);
                transientStreams.Add(converted);
            }

            var finalStream = await _pdfMergeService.MergeAsync(transientStreams, request.PostProcessOptions, cancellationToken);
            _logger.LogInformation("PDF composition completed. Parts merged: {Count}", transientStreams.Count);
            return finalStream;
        }
        finally
        {
            foreach (var stream in transientStreams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    private static string BuildFullHtml(string htmlBody, string? css, string? customFontCss)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\" /><style>");

        if (!string.IsNullOrWhiteSpace(css))
        {
            sb.Append(css);
        }

        if (!string.IsNullOrWhiteSpace(customFontCss))
        {
            sb.Append(customFontCss);
        }

        sb.Append("</style></head><body>");
        sb.Append(htmlBody);
        sb.Append("</body></html>");

        return sb.ToString();
    }
}
