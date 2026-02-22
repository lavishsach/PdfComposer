namespace PdfComposer.Api.Application.Models;

public sealed class GeneratePdfRequest
{
    public required string TemplateHtml { get; init; }
    public string? TemplateCss { get; init; }
    public required System.Text.Json.JsonElement Data { get; init; }
    public PdfRenderOptions RenderOptions { get; init; } = new();
}
