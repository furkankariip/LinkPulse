using LinkPulse.Api.Data;
using LinkPulse.Api.Features.Links;
using LinkPulse.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseSqlite(services.GetRequiredService<IConfiguration>().GetConnectionString("Default")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ShortCodeGenerator>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<LinkService>();

builder.Services.AddSingleton<ClickQueue>();
builder.Services.AddHostedService<ClickRecorder>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddLinkRateLimiting(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.AddOpenApi();

var app = builder.Build();

await app.Services.EnsureDatabaseCreatedAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();

app.MapOpenApi();
app.MapScalarApiReference(options => options.WithTitle("LinkPulse API"));
app.MapHealthChecks("/health");
app.MapLinkEndpoints();

app.Run();

// Exposes the entry point to WebApplicationFactory in the integration tests.
public partial class Program;
