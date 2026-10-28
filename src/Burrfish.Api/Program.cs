using Burrfish.Api.Configuration;
using Burrfish.Api.Endpoints;
using Burrfish.Application;
using Burrfish.Infrastructure;
using Burrfish.Infrastructure.Persistence;
using Burrfish.Infrastructure.Processing;
using Microsoft.EntityFrameworkCore;

var migrateDatabase = args is ["--migrate"];
var builder = WebApplication.CreateBuilder(migrateDatabase ? [] : args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddBurrfishHealthChecks();
builder.Services.AddHostedService<InboxWorker>();
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<DataRetentionWorker>();
builder.Services.AddHostedService<QueueMetricsWorker>();

var app = builder.Build();
if (migrateDatabase)
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<BurrfishDbContext>();
    await db.Database.MigrateAsync();
    return;
}

app.MapBurrfishHealthChecks();
app.MapGet("/webhooks/instagram", InstagramWebhookEndpoints.Verify);
app.MapPost("/webhooks/instagram", InstagramWebhookEndpoints.Receive);
app.Run();

/// <summary>Точка входа приложения, доступная HTTP-контрактным тестам.</summary>
public partial class Program;
