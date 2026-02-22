namespace PdfComposer.Api.Application.Models;

public sealed class ComposeAttachment
{
    public required string FileName { get; init; }
    public required Stream Content { get; init; }
    public string? ContentType { get; init; }
    public SupportedFileType? DeclaredFileType { get; init; }
}
