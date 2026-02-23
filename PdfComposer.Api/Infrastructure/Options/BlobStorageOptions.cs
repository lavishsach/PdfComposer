namespace PdfComposer.Api.Infrastructure.Options;

public sealed class BlobStorageOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "pdf-composer";
    public string? BlobPrefix { get; set; }
}
