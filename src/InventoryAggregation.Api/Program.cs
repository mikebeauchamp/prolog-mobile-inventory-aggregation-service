using InventoryAggregation.Api;
using InventoryAggregation.Core;
using InventoryAggregation.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInventoryInfrastructure(builder.Configuration);
builder.Services.AddSingleton<InventoryAggregator>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.MapInventoryEndpoints();

app.Run();

// Exposes the entry point to the test host (WebApplicationFactory<Program>).
public partial class Program;
