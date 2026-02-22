using PdfComposer.Api.Application.Models;

namespace PdfComposer.Api.Application.Interfaces;

public interface IPdfMergeService
{
    Task<Stream> MergeAsync(IReadOnlyList<Stream> pdfStreams, PdfPostProcessOptions options, CancellationToken cancellationToken = default);
}
