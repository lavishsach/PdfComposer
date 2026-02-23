using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Infrastructure.Options;

namespace PdfComposer.Api.Infrastructure.Services;

public sealed class AzureBlobStorageService(
    IOptions<BlobStorageOptions> options,
    ILogger<AzureBlobStorageService> logger) : IBlobStorageService
{
    private readonly BlobStorageOptions _options = options.Value;
    private readonly ILogger<AzureBlobStorageService> _logger = logger;
    private readonly SemaphoreSlim _containerInitLock = new(1, 1);
    private BlobContainerClient? _containerClient;

    public async Task<Uri> UploadPdfAsync(Stream content, string blobName, CancellationToken cancellationToken = default)
    {
        ValidateOptions();

        var container = await GetContainerClientAsync(cancellationToken);
        var normalizedBlobName = NormalizeBlobName(blobName);
        var blobClient = container.GetBlobClient(normalizedBlobName);

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        await blobClient.UploadAsync(content, overwrite: true, cancellationToken);
        _logger.LogInformation("Uploaded PDF to blob storage. BlobName: {BlobName}", normalizedBlobName);
        return blobClient.Uri;
    }

    private async Task<BlobContainerClient> GetContainerClientAsync(CancellationToken cancellationToken)
    {
        if (_containerClient is not null)
        {
            return _containerClient;
        }

        await _containerInitLock.WaitAsync(cancellationToken);
        try
        {
            if (_containerClient is not null)
            {
                return _containerClient;
            }

            var serviceClient = new BlobServiceClient(_options.ConnectionString);
            var containerClient = serviceClient.GetBlobContainerClient(_options.ContainerName);
            await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _containerClient = containerClient;
            return containerClient;
        }
        finally
        {
            _containerInitLock.Release();
        }
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("BlobStorage:ConnectionString is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.ContainerName))
        {
            throw new InvalidOperationException("BlobStorage:ContainerName is not configured.");
        }
    }

    private string NormalizeBlobName(string blobName)
    {
        var trimmed = blobName.TrimStart('/');
        if (string.IsNullOrWhiteSpace(_options.BlobPrefix))
        {
            return trimmed;
        }

        return $"{_options.BlobPrefix.Trim('/')}/{trimmed}";
    }
}
