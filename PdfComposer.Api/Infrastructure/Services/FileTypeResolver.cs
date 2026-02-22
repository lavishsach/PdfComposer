using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;

namespace PdfComposer.Api.Infrastructure.Services;

public sealed class FileTypeResolver : IFileTypeResolver
{
    public SupportedFileType ResolveFromExtension(string fileNameOrExtension)
    {
        var normalized = Path.GetExtension(fileNameOrExtension)?.Trim().TrimStart('.').ToLowerInvariant();

        return normalized switch
        {
            "pdf" => SupportedFileType.Pdf,
            "doc" => SupportedFileType.Doc,
            "docx" => SupportedFileType.Docx,
            "csv" => SupportedFileType.Csv,
            "xls" => SupportedFileType.Xls,
            "xlsx" => SupportedFileType.Xlsx,
            "png" => SupportedFileType.Png,
            "jpg" => SupportedFileType.Jpg,
            "jpeg" => SupportedFileType.Jpeg,
            "txt" => SupportedFileType.Txt,
            _ => SupportedFileType.Unknown
        };
    }

    public SupportedFileType ResolveFromString(string fileType)
    {
        if (string.IsNullOrWhiteSpace(fileType))
        {
            return SupportedFileType.Unknown;
        }

        var trimmed = fileType.Trim();
        if (trimmed.Contains('/'))
        {
            var ext = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
            return ext is null ? SupportedFileType.Unknown : ResolveFromExtension($".{ext}");
        }

        return ResolveFromExtension(trimmed.StartsWith('.') ? trimmed : $".{trimmed}");
    }
}
