namespace PdfComposer.Api.Infrastructure.Utilities;

public static class TempStreamFactory
{
    public static async Task<Stream> CreateFromBytesAsync(byte[] bytes, string extension, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pdf-composer-{Guid.NewGuid():N}{extension}");
        var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        await stream.WriteAsync(bytes, cancellationToken);
        stream.Position = 0;
        return stream;
    }

    public static async Task<Stream> CopyToTempAsync(Stream input, string extension, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pdf-composer-{Guid.NewGuid():N}{extension}");
        var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        if (input.CanSeek)
        {
            input.Position = 0;
        }

        await input.CopyToAsync(stream, cancellationToken);
        stream.Position = 0;
        return stream;
    }
}
