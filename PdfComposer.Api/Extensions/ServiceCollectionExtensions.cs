using PdfComposer.Api.Application.Interfaces;
using PdfComposer.Api.Infrastructure.Converters;
using PdfComposer.Api.Infrastructure.Options;
using PdfComposer.Api.Infrastructure.Services;

namespace PdfComposer.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPdfComposer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PdfComposerOptions>(configuration.GetSection("PdfComposer"));
        services.Configure<BlobStorageOptions>(configuration.GetSection("BlobStorage"));

        services.AddScoped<IPdfComposerService, PdfComposerService>();
        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();
        services.AddSingleton<IHtmlTemplateEngine, RazorHtmlTemplateEngine>();
        services.AddSingleton<IHtmlToPdfConverter, DinkToPdfHtmlConverter>();
        services.AddSingleton<IPdfMergeService, ITextPdfMergeService>();
        services.AddSingleton<IFileTypeResolver, FileTypeResolver>();

        services.AddScoped<IFileToPdfConverter, PdfPassthroughConverter>();
        services.AddScoped<IFileToPdfConverter, DocToPdfConverter>();
        services.AddScoped<IFileToPdfConverter, CsvToPdfConverter>();
        services.AddScoped<IFileToPdfConverter, ExcelToPdfConverter>();
        services.AddScoped<IFileToPdfConverter, ImageToPdfConverter>();
        services.AddScoped<IFileToPdfConverter, TextToPdfConverter>();

        return services;
    }
}
