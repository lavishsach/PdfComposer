namespace PdfComposer.Api.Application.Interfaces;

public interface IBlobStorageService
{
    Task<Uri> UploadPdfAsync(Stream content, string blobName, CancellationToken cancellationToken = default);
}
