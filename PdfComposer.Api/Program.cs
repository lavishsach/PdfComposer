using DinkToPdf;
using DinkToPdf.Contracts;
using PdfComposer.Api;
using PdfComposer.Api.Extensions;
using PdfComposer.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Load native wkhtmltox before any service/instance that depends on it
NativeLibraryLoader.LoadWkhtmltox();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddPdfComposer(builder.Configuration);

// now safe to register converter
builder.Services.AddSingleton(typeof(IConverter), new SynchronizedConverter(new PdfTools()));

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
