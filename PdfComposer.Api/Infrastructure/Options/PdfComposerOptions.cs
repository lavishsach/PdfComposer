namespace PdfComposer.Api.Infrastructure.Options;

public sealed class PdfComposerOptions
{
    public string LibreOfficeBinaryPath { get; set; } = "soffice";
    public bool UseDockerForLibreOffice { get; set; }
    public string LibreOfficeDockerExecutable { get; set; } = "docker";
    public string LibreOfficeContainerName { get; set; } = "libreoffice";
    public int DocToPdfTimeoutSeconds { get; set; } = 120;
}
