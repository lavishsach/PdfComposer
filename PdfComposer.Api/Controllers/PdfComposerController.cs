using Microsoft.AspNetCore.Mvc;
using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using System.Text.Json;

namespace PdfComposer.Api.Controllers;

[ApiController]
[Route("")]
public sealed class PdfComposerController(
    IPdfComposerService pdfComposerService,
    IBlobStorageService blobStorageService,
    IFileTypeResolver fileTypeResolver,
    ILogger<PdfComposerController> logger) : ControllerBase
{
    private readonly IPdfComposerService _pdfComposerService = pdfComposerService;
    private readonly IBlobStorageService _blobStorageService = blobStorageService;
    private readonly IFileTypeResolver _fileTypeResolver = fileTypeResolver;
    private readonly ILogger<PdfComposerController> _logger = logger;

    [HttpPost("generate")]
    [Consumes("application/json")]
    public async Task<IActionResult> GenerateAsync([FromBody] GeneratePdfRequest request, CancellationToken cancellationToken)
    {
        var outputStream = await _pdfComposerService.GenerateFromTemplateAsync(
            request.TemplateHtml,
            request.Data,
            request.TemplateCss,
            request.RenderOptions,
            cancellationToken);

        if (outputStream.CanSeek)
        {
            outputStream.Position = 0;
        }

        return File(outputStream, "application/pdf", $"generated-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf");
    }

    [HttpPost("generate/blob")]
    [Consumes("application/json")]
    public async Task<ActionResult<BlobUploadResponse>> GenerateToBlobAsync([FromBody] GeneratePdfRequest request, CancellationToken cancellationToken)
    {
        await using var outputStream = await _pdfComposerService.GenerateFromTemplateAsync(
            request.TemplateHtml,
            request.Data,
            request.TemplateCss,
            request.RenderOptions,
            cancellationToken);

        var blobName = $"generated/{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.pdf";
        var uri = await _blobStorageService.UploadPdfAsync(outputStream, blobName, cancellationToken);

        return Ok(new BlobUploadResponse
        {
            BlobName = blobName,
            BlobUri = uri
        });
    }

    [HttpPost("compose")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ComposeAsync(
        [FromForm] string metadata,
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        await using var buildContext = BuildComposeContext(metadata, files);
        var outputStream = await _pdfComposerService.ComposeFinalPdfAsync(buildContext.Request, cancellationToken);
        if (outputStream.CanSeek)
        {
            outputStream.Position = 0;
        }

        _logger.LogInformation("Compose request processed. Attachments: {Count}", buildContext.AttachmentCount);
        return File(outputStream, "application/pdf", $"composed-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf");
    }

    [HttpPost("compose/blob")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<BlobUploadResponse>> ComposeToBlobAsync(
        [FromForm] string metadata,
        [FromForm] List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        await using var buildContext = BuildComposeContext(metadata, files);
        await using var outputStream = await _pdfComposerService.ComposeFinalPdfAsync(buildContext.Request, cancellationToken);

        var blobName = $"composed/{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.pdf";
        var uri = await _blobStorageService.UploadPdfAsync(outputStream, blobName, cancellationToken);

        _logger.LogInformation("Compose-to-blob request processed. Attachments: {Count}", buildContext.AttachmentCount);
        return Ok(new BlobUploadResponse
        {
            BlobName = blobName,
            BlobUri = uri
        });
    }

    private ComposeBuildContext BuildComposeContext(string metadata, List<IFormFile> files)
    {
        var meta = JsonSerializer.Deserialize<ComposeMetadataRequest>(metadata, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Invalid compose metadata payload.");

        var openedStreams = new List<Stream>();
        try
        {
            var attachments = new List<ComposeAttachment>();
            foreach (var attachmentSpec in meta.Attachments)
            {
                var file = files.FirstOrDefault(f =>
                    string.Equals(f.Name, attachmentSpec.Key, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(f.FileName, attachmentSpec.Key, StringComparison.OrdinalIgnoreCase));

                if (file is null)
                {
                    throw new InvalidOperationException($"Attachment '{attachmentSpec.Key}' was not provided.");
                }

                var stream = file.OpenReadStream();
                openedStreams.Add(stream);

                SupportedFileType? declaredType = string.IsNullOrWhiteSpace(attachmentSpec.FileType)
                    ? null
                    : _fileTypeResolver.ResolveFromString(attachmentSpec.FileType);

                attachments.Add(new ComposeAttachment
                {
                    FileName = file.FileName,
                    Content = stream,
                    ContentType = file.ContentType,
                    DeclaredFileType = declaredType
                });
            }

            var composeRequest = new ComposeFinalPdfRequest
            {
                TemplateHtml = meta.TemplateHtml,
                TemplateCss = meta.TemplateCss,
                Data = meta.Data,
                Attachments = attachments,
                RenderOptions = meta.RenderOptions,
                PostProcessOptions = meta.PostProcessOptions
            };

            return new ComposeBuildContext(composeRequest, openedStreams, attachments.Count);
        }
        catch
        {
            foreach (var stream in openedStreams)
            {
                stream.Dispose();
            }

            throw;
        }
    }

    private sealed class ComposeBuildContext(ComposeFinalPdfRequest request, List<Stream> openedStreams, int attachmentCount) : IAsyncDisposable
    {
        public ComposeFinalPdfRequest Request { get; } = request;
        public int AttachmentCount { get; } = attachmentCount;

        public async ValueTask DisposeAsync()
        {
            foreach (var stream in openedStreams)
            {
                await stream.DisposeAsync();
            }
        }
    }
}
