using CsvHelper;
using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Application.Models;
using PdfComposer.Api.Infrastructure.Options;
using PdfComposer.Api.Infrastructure.Utilities;
using System.Diagnostics;
using System.Globalization;

namespace PdfComposer.Api.Infrastructure.Converters;

public sealed class CsvToPdfConverter(
    IOptions<PdfComposerOptions> options,
    ILogger<CsvToPdfConverter> logger) : IFileToPdfConverter
{
    private static readonly string[] CommonWindowsSofficePaths =
    [
        @"C:\Program Files\LibreOffice\program\soffice.exe",
        @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
    ];

    private readonly PdfComposerOptions _options = options.Value;
    private readonly ILogger<CsvToPdfConverter> _logger = logger;

    public bool CanConvert(SupportedFileType fileType) => fileType == SupportedFileType.Csv;

    public async Task<Stream> ConvertAsync(Stream input, string fileName, CancellationToken cancellationToken = default)
    {
        ValidateOptions();

        var workingDir = Path.Combine(Path.GetTempPath(), $"csv-convert-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDir);

        try
        {
            var sourcePath = Path.Combine(workingDir, "source.xlsx");
            var outputPath = Path.Combine(workingDir, "source.pdf");

            await BuildStyledWorkbookFromCsvAsync(input, sourcePath, cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.DocToPdfTimeoutSeconds));

            if (_options.UseDockerForLibreOffice)
            {
                await ConvertViaDockerAsync(sourcePath, outputPath, timeoutCts.Token);
            }
            else
            {
                await ConvertViaHostBinaryAsync(sourcePath, workingDir, timeoutCts.Token);
            }

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
                $"CSV LibreOffice conversion timed out after {_options.DocToPdfTimeoutSeconds} seconds while running '{ex.FileName} {ex.Arguments}'. " +
                $"Partial StdOut: {Truncate(ex.StdOut)} Partial StdErr: {Truncate(ex.StdErr)}",
                ex);
        }
        catch (ProcessCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                $"CSV conversion was canceled while running '{ex.FileName} {ex.Arguments}'.",
                ex,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CSV conversion failed for {FileName}", fileName);
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

    private async Task ConvertViaHostBinaryAsync(string sourcePath, string workingDir, CancellationToken cancellationToken)
    {
        var sofficeExecutable = ResolveSofficeExecutable();
        var args = $"--headless --convert-to pdf --outdir \"{workingDir}\" \"{sourcePath}\"";

        EnsureSuccess(
            await RunProcessAsync(sofficeExecutable, args, cancellationToken),
            "host LibreOffice conversion");
    }

    private async Task ConvertViaDockerAsync(string sourcePath, string outputPath, CancellationToken cancellationToken)
    {
        var containerInput = $"/tmp/pdf-composer-{Guid.NewGuid():N}.xlsx";
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
                "CSV conversion process canceled. FileName: {FileName}, Arguments: {Arguments}, PartialStdOut: {StdOut}, PartialStdErr: {StdErr}",
                fileName,
                arguments,
                Truncate(canceledStdOut),
                Truncate(canceledStdErr));

            throw new ProcessCanceledException(fileName, arguments, canceledStdOut, canceledStdErr, ex);
        }

        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        _logger.LogInformation(
            "CSV conversion process finished. FileName: {FileName}, ExitCode: {ExitCode}, Arguments: {Arguments}, StdOut: {StdOut}, StdErr: {StdErr}",
            fileName,
            process.ExitCode,
            arguments,
            Truncate(stdOut),
            Truncate(stdErr));

        return new ProcessResult(process.ExitCode, stdOut, stdErr);
    }

    private static async Task BuildStyledWorkbookFromCsvAsync(Stream csvInput, string xlsxOutputPath, CancellationToken cancellationToken)
    {
        if (csvInput.CanSeek)
        {
            csvInput.Position = 0;
        }

        using var reader = new StreamReader(csvInput, leaveOpen: true);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Data");

        var parsedRows = new List<string[]>();
        var detectedMaxCols = 0;
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = csv.Parser.Record;
            if (record is null || record.Length == 0)
            {
                continue;
            }

            var lastNonEmptyIndex = -1;
            for (var i = record.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(record[i]))
                {
                    lastNonEmptyIndex = i;
                    break;
                }
            }

            if (lastNonEmptyIndex < 0)
            {
                continue;
            }

            var rowValues = new string[lastNonEmptyIndex + 1];
            for (var i = 0; i <= lastNonEmptyIndex; i++)
            {
                rowValues[i] = record[i]?.Trim() ?? string.Empty;
            }

            parsedRows.Add(rowValues);
            detectedMaxCols = Math.Max(detectedMaxCols, rowValues.Length);
        }

        if (parsedRows.Count == 0 || detectedMaxCols == 0)
        {
            sheet.Cell(1, 1).Value = "No CSV data";
            workbook.SaveAs(xlsxOutputPath);
            return;
        }

        // Keep only columns that contain data in at least one row.
        var usedColumns = new bool[detectedMaxCols];
        foreach (var rowValues in parsedRows)
        {
            for (var i = 0; i < rowValues.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(rowValues[i]))
                {
                    usedColumns[i] = true;
                }
            }
        }

        var columnMap = new List<int>(detectedMaxCols);
        for (var i = 0; i < detectedMaxCols; i++)
        {
            if (usedColumns[i])
            {
                columnMap.Add(i);
            }
        }

        if (columnMap.Count == 0)
        {
            sheet.Cell(1, 1).Value = "No CSV data";
            workbook.SaveAs(xlsxOutputPath);
            return;
        }

        var row = 1;
        foreach (var rowValues in parsedRows)
        {
            var hasData = false;
            for (var outCol = 0; outCol < columnMap.Count; outCol++)
            {
                var sourceCol = columnMap[outCol];
                var value = sourceCol < rowValues.Length ? rowValues[sourceCol] : string.Empty;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    hasData = true;
                    break;
                }
            }

            if (!hasData)
            {
                continue;
            }

            for (var outCol = 0; outCol < columnMap.Count; outCol++)
            {
                var sourceCol = columnMap[outCol];
                var value = sourceCol < rowValues.Length ? rowValues[sourceCol] : string.Empty;
                sheet.Cell(row, outCol + 1).Value = value;
            }

            row++;
        }

        var maxCols = columnMap.Count;
        var lastRow = row - 1;
        var dataRange = sheet.Range(1, 1, lastRow, maxCols);
        dataRange.Style.Alignment.WrapText = false;
        dataRange.Style.Alignment.ShrinkToFit = false;
        dataRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        dataRange.Style.Font.FontSize = 10;
        dataRange.Style.Fill.BackgroundColor = XLColor.White;

        var headerRange = sheet.Range(1, 1, 1, maxCols);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        headerRange.Style.Alignment.WrapText = false;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

        // Width strategy:
        // - If table comfortably fits page width: keep single-line cells (no wrap).
        // - If table is too wide: proportionally shrink columns and allow wrapping for text cells.
        var preferredWidths = new double[maxCols];
        for (var c = 1; c <= maxCols; c++)
        {
            var maxLen = 0;
            for (var r = 1; r <= lastRow; r++)
            {
                var text = sheet.Cell(r, c).GetString();
                if (text.Length > maxLen)
                {
                    maxLen = text.Length;
                }
            }

            // Character-based width estimate with padding.
            preferredWidths[c - 1] = Math.Clamp(maxLen + 2d, 8d, 40d);
        }

        var preferredTotal = preferredWidths.Sum();
        const double printableWidthChars = 95d;
        var needsWrap = preferredTotal > printableWidthChars;

        if (!needsWrap)
        {
            for (var c = 1; c <= maxCols; c++)
            {
                sheet.Column(c).Width = preferredWidths[c - 1];
            }
            dataRange.Style.Alignment.WrapText = false;
            headerRange.Style.Alignment.WrapText = false;
        }
        else
        {
            var scale = printableWidthChars / preferredTotal;
            for (var c = 1; c <= maxCols; c++)
            {
                var scaled = preferredWidths[c - 1] * scale;
                sheet.Column(c).Width = Math.Clamp(scaled, 6.5d, 22d);
            }

            // Allow wrap only when needed due to width constraints.
            dataRange.Style.Alignment.WrapText = true;
            headerRange.Style.Alignment.WrapText = true;
        }

        sheet.Row(1).Height = 24;
        for (var r = 2; r <= lastRow; r++)
        {
            sheet.Row(r).Height = 18;
        }

        sheet.Rows(1, lastRow).AdjustToContents();
        sheet.PageSetup.Margins.Top = 0.5;
        sheet.PageSetup.Margins.Bottom = 0.5;
        sheet.PageSetup.Margins.Left = 0.5;
        sheet.PageSetup.Margins.Right = 0.5;
        sheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        sheet.PageSetup.CenterHorizontally = true;
        sheet.PageSetup.Header.Center.AddText(string.Empty);
        sheet.PageSetup.Footer.Center.AddText(string.Empty);

        workbook.SaveAs(xlsxOutputPath);
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

        if (string.IsNullOrWhiteSpace(_options.LibreOfficeBinaryPath))
        {
            throw new InvalidOperationException("PdfComposer:LibreOfficeBinaryPath is not configured.");
        }

        if (_options.UseDockerForLibreOffice)
        {
            if (string.IsNullOrWhiteSpace(_options.LibreOfficeDockerExecutable))
            {
                throw new InvalidOperationException("PdfComposer:LibreOfficeDockerExecutable is not configured.");
            }

            if (string.IsNullOrWhiteSpace(_options.LibreOfficeContainerName))
            {
                throw new InvalidOperationException("PdfComposer:LibreOfficeContainerName is not configured.");
            }
        }
    }

    private string ResolveSofficeExecutable()
    {
        var configured = _options.LibreOfficeBinaryPath.Trim();

        if (LooksLikePath(configured))
        {
            if (File.Exists(configured))
            {
                return configured;
            }

            throw new FileNotFoundException(
                "Configured LibreOffice binary was not found. Set PdfComposer:LibreOfficeBinaryPath to a valid soffice executable path.",
                configured);
        }

        var fromPath = ResolveExecutableFromPath(configured);
        if (fromPath is not null)
        {
            return fromPath;
        }

        foreach (var candidate in CommonWindowsSofficePaths)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"LibreOffice executable '{configured}' was not found in PATH or common install locations. " +
            "Install LibreOffice or set PdfComposer:LibreOfficeBinaryPath to the full soffice.exe path.");
    }

    private static bool LooksLikePath(string value)
        => value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar);

    private static string? ResolveExecutableFromPath(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        var pathext = Environment.GetEnvironmentVariable("PATHEXT")?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [".exe", ".cmd", ".bat"];

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directCandidate = Path.Combine(directory, executableName);
            if (File.Exists(directCandidate))
            {
                return directCandidate;
            }

            if (Path.HasExtension(executableName))
            {
                continue;
            }

            foreach (var ext in pathext)
            {
                var candidate = Path.Combine(directory, executableName + ext);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
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
