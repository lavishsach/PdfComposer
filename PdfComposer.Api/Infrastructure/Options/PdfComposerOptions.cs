namespace PdfComposer.Api.Infrastructure.Options;

public sealed class PdfComposerOptions
{
    public string LibreOfficeDockerExecutable { get; set; } = "docker";
    public string LibreOfficeContainerName { get; set; } = "libreoffice";
    public int DocToPdfTimeoutSeconds { get; set; } = 120;
}
