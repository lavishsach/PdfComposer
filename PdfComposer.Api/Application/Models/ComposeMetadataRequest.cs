namespace PdfComposer.Api.Application.Models;

public sealed class ComposeMetadataRequest
{
    public required string TemplateHtml { get; init; }
    public string? TemplateCss { get; init; }
    public required System.Text.Json.JsonElement Data { get; init; }
    public List<ComposeAttachmentDescriptor> Attachments { get; init; } = [];
    public PdfRenderOptions RenderOptions { get; init; } = new();
    public PdfPostProcessOptions PostProcessOptions { get; init; } = new();
}

public sealed class ComposeAttachmentDescriptor
{
    public required string Key { get; init; }
    public string? FileType { get; init; }
}
