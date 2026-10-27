using KnowledgeRag.Api.Endpoints;
using KnowledgeRag.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddApiServices(builder.Configuration);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64L * 1024L * 1024L);

var app = builder.Build();

app.UseApiPipeline();
await app.Services.InitializeInfrastructureAsync(builder.Configuration);
app.MapApiEndpoints(builder.Configuration);
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
