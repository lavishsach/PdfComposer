using PdfComposer.Api.Application.Models;

namespace PdfComposer.Api.Application.Interfaces;

public interface IFileTypeResolver
{
    SupportedFileType ResolveFromExtension(string fileNameOrExtension);
    SupportedFileType ResolveFromString(string fileType);
}
