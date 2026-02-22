using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using PdfComposer.Api.Infrastructure.Utilities;

namespace PdfComposer.Api.Infrastructure.Converters;

public sealed class PdfPassthroughConverter : IFileToPdfConverter
{
    public bool CanConvert(SupportedFileType fileType) => fileType == SupportedFileType.Pdf;

    public Task<Stream> ConvertAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
        => TempStreamFactory.CopyToTempAsync(input, ".pdf", cancellationToken);
}
