using Microsoft.Extensions.Options;
using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using PdfComposer.Api.Infrastructure.Options;
using PdfComposer.Api.Infrastructure.Utilities;
using System.Diagnostics;

namespace PdfComposer.Api.Infrastructure.Converters;

public sealed class DocToPdfConverter(
    IOptions<PdfComposerOptions> options,
    ILogger<DocToPdfConverter> logger) : IFileToPdfConverter
{
    private readonly PdfComposerOptions _options = options.Value;
    private readonly ILogger<DocToPdfConverter> _logger = logger;

    public bool CanConvert(SupportedFileType fileType)
        => fileType is SupportedFileType.Doc or SupportedFileType.Docx;

    public async Task<Stream> ConvertAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
    {
        var workingDir = Path.Combine(Path.GetTempPath(), $"doc-convert-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDir);

        try
        {
            var extension = Path.GetExtension(fileName);
            var sourcePath = Path.Combine(workingDir, $"source{extension}");

            await using (var source = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                if (input.CanSeek)
                {
                    input.Position = 0;
                }

                await input.CopyToAsync(source, cancellationToken);
            }

            var args = $"--headless --convert-to pdf --outdir \"{workingDir}\" \"{sourcePath}\"";
            var processInfo = new ProcessStartInfo
            {
                FileName = _options.LibreOfficeBinaryPath,
                Arguments = args,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo)
                ?? throw new InvalidOperationException("Unable to start LibreOffice process.");

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var stdErr = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"LibreOffice conversion failed: {stdErr}");
            }

            var pdfPath = Path.Combine(workingDir, "source.pdf");
            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("Expected converted PDF output not found.", pdfPath);
            }

            await using var converted = new FileStream(pdfPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            return await TempStreamFactory.CopyToTempAsync(converted, ".pdf", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DOC/DOCX conversion failed for {FileName}", fileName);
            throw;
        }
        finally
        {
            if (Directory.Exists(workingDir))
            {
                Directory.Delete(workingDir, recursive: true);
            }
        }
    }
}
