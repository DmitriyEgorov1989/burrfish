using Burrfish.Api.Endpoints;
using Burrfish.Application;
using Burrfish.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();
app.MapGet("/webhooks/instagram", InstagramWebhookEndpoints.Verify);
app.MapPost("/webhooks/instagram", InstagramWebhookEndpoints.Receive);
app.Run();

/// <summary>Точка входа приложения, доступная HTTP-контрактным тестам.</summary>
public partial class Program;
