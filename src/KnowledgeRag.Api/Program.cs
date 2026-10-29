using KnowledgeRag.Api.Endpoints;
using KnowledgeRag.Api.Extensions;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);

var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
if (ragOptions.MaxUploadMegabytes is < 1 or > 63)
    throw new InvalidOperationException("Rag:MaxUploadMegabytes 必须在 1 到 63 之间，以满足应用请求体上限。");
if (ragOptions.MaxExtractedCharacters is < 1 or > 10_000_000)
    throw new InvalidOperationException("Rag:MaxExtractedCharacters 必须在 1 到 10000000 之间。");
if (ragOptions.MaxPdfPages is < 1 or > 10_000)
    throw new InvalidOperationException("Rag:MaxPdfPages 必须在 1 到 10000 之间。");

var maxUploadBytes = ragOptions.MaxUploadMegabytes * 1024L * 1024L;
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxUploadBytes);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxUploadBytes + 1024L * 1024L);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddApiServices(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseApiPipeline();
await app.Services.InitializeInfrastructureAsync(builder.Configuration);
app.MapApiEndpoints(builder.Configuration);
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
