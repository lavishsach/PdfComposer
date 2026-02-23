# PdfComposer

ASP.NET Core API for composing final PDFs from:
- Razor/HTML templates
- Input attachments (`pdf`, `doc`, `docx`, `csv`, `xlsx`, images, `txt`)

## DOC/DOCX to PDF (OnlyOffice)

`doc` and `docx` attachments are converted with **ONLYOFFICE DocumentBuilder**.
LibreOffice is no longer used in the conversion pipeline.

### 1. Install OnlyOffice DocumentBuilder

Install ONLYOFFICE DocumentBuilder on the API host and confirm the binary is executable:

```powershell
documentbuilder --help
```

If the command is not in `PATH`, use the full executable path in configuration.

### 2. Configure appsettings

Set these values under `PdfComposer`:

```json
{
  "PdfComposer": {
    "OnlyOfficeDocumentBuilderPath": "documentbuilder",
    "DocToPdfTimeoutSeconds": 120
  }
}
```

Fields:
- `OnlyOfficeDocumentBuilderPath`: command name in `PATH` or full executable path.
- `DocToPdfTimeoutSeconds`: max conversion runtime before timeout.

### 3. End-to-end conversion flow

When a `doc`/`docx` file is attached:
1. API stores attachment in a temp working directory.
2. API generates a `convert.docbuilder` script.
3. API runs `documentbuilder` with that script.
4. Script opens source file and saves it as PDF.
5. API validates the generated PDF exists.
6. PDF stream is returned to the composer pipeline.
7. Final document is merged with other PDF parts and post-processed.

### 4. Script used for conversion

The converter generates a script with this logic:

```javascript
builder.OpenFile("C:\\temp\\source.docx", "");
builder.SaveFile("pdf", "C:\\temp\\source.pdf");
builder.CloseFile();
```

### 5. Compose API request example

Use `POST /compose` as `multipart/form-data`:
- form field `metadata`: JSON payload
- form files matching each attachment `key`

Example `metadata` value:

```json
{
  "templateHtml": "<h1>Invoice</h1>",
  "data": { "customer": "Acme" },
  "attachments": [
    { "key": "docPart", "fileType": "docx" },
    { "key": "pdfPart", "fileType": "pdf" }
  ]
}
```

See `examples/compose-metadata.json` and corresponding files in `examples/`.

### 6. Operational notes

- Ensure the API process user can execute `documentbuilder`.
- Ensure temp directory read/write permissions are available.
- If conversion fails, check API logs for:
  - DocumentBuilder exit code
  - `StdOut` / `StdErr`
  - timeout exceptions
