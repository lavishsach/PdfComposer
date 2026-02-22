namespace PdfComposer.Api.Application.Models;

public sealed class ComposeFinalPdfRequest
{
    public required string TemplateHtml { get; init; }
    public string? TemplateCss { get; init; }
    public required System.Text.Json.JsonElement Data { get; init; }
    public required IReadOnlyList<ComposeAttachment> Attachments { get; init; }
    public PdfRenderOptions RenderOptions { get; init; } = new();
    public PdfPostProcessOptions PostProcessOptions { get; init; } = new();
}
