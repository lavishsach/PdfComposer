using PdfComposer.Api.Application.Models;

namespace PdfComposer.Api.Application.Interfaces;

public interface IFileToPdfConverter
{
    bool CanConvert(SupportedFileType fileType);
    Task<Stream> ConvertAsync(Stream input, string fileName, CancellationToken cancellationToken = default);
}
