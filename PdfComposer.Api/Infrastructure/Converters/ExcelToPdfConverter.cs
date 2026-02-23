using Microsoft.Extensions.Options;
using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using PdfComposer.Api.Infrastructure.Options;
using PdfComposer.Api.Infrastructure.Utilities;
using System.Diagnostics;

namespace PdfComposer.Api.Infrastructure.Converters;

public sealed class ExcelToPdfConverter(
    IOptions<PdfComposerOptions> options,
    ILogger<ExcelToPdfConverter> logger) : IFileToPdfConverter
{
    private readonly PdfComposerOptions _options = options.Value;
    private readonly ILogger<ExcelToPdfConverter> _logger = logger;

    public bool CanConvert(SupportedFileType fileType)
        => fileType is SupportedFileType.Xls or SupportedFileType.Xlsx;

    public async Task<Stream> ConvertAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
    {
        ValidateOptions();

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".xlsx";
        }

        var workingDir = Path.Combine(Path.GetTempPath(), $"excel-convert-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDir);

        try
        {
            var sourcePath = Path.Combine(workingDir, $"source{extension}");
            var outputPath = Path.Combine(workingDir, "source.pdf");

            await using (var source = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                if (input.CanSeek)
                {
                    input.Position = 0;
                }

                await input.CopyToAsync(source, cancellationToken);
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.DocToPdfTimeoutSeconds));

            await ConvertViaDockerAsync(sourcePath, outputPath, timeoutCts.Token);

            if (!File.Exists(outputPath))
            {
                throw new FileNotFoundException("Expected converted PDF output not found.", outputPath);
            }

            await using var converted = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            return await TempStreamFactory.CopyToTempAsync(converted, ".pdf", cancellationToken);
        }
        catch (ProcessCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Excel LibreOffice conversion timed out after {_options.DocToPdfTimeoutSeconds} seconds while running '{ex.FileName} {ex.Arguments}'. " +
                $"Partial StdOut: {Truncate(ex.StdOut)} Partial StdErr: {Truncate(ex.StdErr)}",
                ex);
        }
        catch (ProcessCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                $"Excel conversion was canceled while running '{ex.FileName} {ex.Arguments}'.",
                ex,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excel conversion failed for {FileName}", fileName);
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

    private async Task ConvertViaDockerAsync(string sourcePath, string outputPath, CancellationToken cancellationToken)
    {
        var containerInput = $"/tmp/pdf-composer-{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}";
        var containerOutput = Path.ChangeExtension(containerInput, ".pdf");

        EnsureSuccess(
            await RunProcessAsync(
                _options.LibreOfficeDockerExecutable,
                $"cp \"{sourcePath}\" \"{_options.LibreOfficeContainerName}:{containerInput}\"",
                cancellationToken),
            "docker copy input");

        EnsureSuccess(
            await RunProcessAsync(
                _options.LibreOfficeDockerExecutable,
                $"exec {_options.LibreOfficeContainerName} soffice --headless --convert-to pdf --outdir /tmp \"{containerInput}\"",
                cancellationToken),
            "docker LibreOffice conversion");

        EnsureSuccess(
            await RunProcessAsync(
                _options.LibreOfficeDockerExecutable,
                $"cp \"{_options.LibreOfficeContainerName}:{containerOutput}\" \"{outputPath}\"",
                cancellationToken),
            "docker copy output");

        _ = RunProcessAsync(
            _options.LibreOfficeDockerExecutable,
            $"exec {_options.LibreOfficeContainerName} sh -c \"rm -f '{containerInput}' '{containerOutput}'\"",
            CancellationToken.None);
    }

    private async Task<ProcessResult> RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException($"Unable to start process '{fileName}'.");

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException ex)
        {
            TryKillProcess(process);
            var canceledStdOut = await stdOutTask;
            var canceledStdErr = await stdErrTask;

            _logger.LogWarning(
                ex,
                "Excel conversion process canceled. FileName: {FileName}, Arguments: {Arguments}, PartialStdOut: {StdOut}, PartialStdErr: {StdErr}",
                fileName,
                arguments,
                Truncate(canceledStdOut),
                Truncate(canceledStdErr));

            throw new ProcessCanceledException(fileName, arguments, canceledStdOut, canceledStdErr, ex);
        }

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        _logger.LogInformation(
            "Excel conversion process finished. FileName: {FileName}, ExitCode: {ExitCode}, Arguments: {Arguments}, StdOut: {StdOut}, StdErr: {StdErr}",
            fileName,
            process.ExitCode,
            arguments,
            Truncate(stdOut),
            Truncate(stdErr));

        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void EnsureSuccess(ProcessResult result, string operation)
    {
        if (result.ExitCode == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"LibreOffice {operation} failed. ExitCode: {result.ExitCode}. StdOut: {result.StdOut}. StdErr: {result.StdErr}");
    }

    private void ValidateOptions()
    {
        if (_options.DocToPdfTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException("PdfComposer:DocToPdfTimeoutSeconds must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(_options.LibreOfficeDockerExecutable))
        {
            throw new InvalidOperationException("PdfComposer:LibreOfficeDockerExecutable is not configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.LibreOfficeContainerName))
        {
            throw new InvalidOperationException("PdfComposer:LibreOfficeContainerName is not configured.");
        }
    }

    private static string Truncate(string value)
    {
        const int max = 2000;
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, max), "...(truncated)");
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

    private sealed class ProcessCanceledException(
        string fileName,
        string arguments,
        string stdOut,
        string stdErr,
        Exception innerException) : Exception("Process execution canceled.", innerException)
    {
        public string FileName { get; } = fileName;
        public string Arguments { get; } = arguments;
        public string StdOut { get; } = stdOut;
        public string StdErr { get; } = stdErr;
    }
}
