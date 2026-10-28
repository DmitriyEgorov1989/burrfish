using Burrfish.Api.Configuration;
using Burrfish.Api.Endpoints;
using Burrfish.Application;
using Burrfish.Infrastructure;
using Burrfish.Infrastructure.Processing;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddBurrfishHealthChecks();
builder.Services.AddHostedService<InboxWorker>();
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<DataRetentionWorker>();
builder.Services.AddHostedService<QueueMetricsWorker>();

var app = builder.Build();
app.MapBurrfishHealthChecks();
app.MapGet("/webhooks/instagram", InstagramWebhookEndpoints.Verify);
app.MapPost("/webhooks/instagram", InstagramWebhookEndpoints.Receive);
app.Run();

/// <summary>Точка входа приложения, доступная HTTP-контрактным тестам.</summary>
public partial class Program;
