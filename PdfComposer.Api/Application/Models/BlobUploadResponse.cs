namespace PdfComposer.Api.Application.Models;

public sealed class BlobUploadResponse
{
    public string BlobName { get; init; } = string.Empty;
    public Uri BlobUri { get; init; } = new("about:blank");
}
