using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using PLC.Shared.Ipc;
using PLC.Shared.Models;
using PLC.Shared.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine("data", "logs", "supervisor-.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

// Services
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddSingleton<FleetService>();
builder.Services.AddSingleton<ConfigPersistence>();
builder.Services.AddSingleton<PlcProcessManager>();
builder.Services.AddSingleton<ElevatorClient>();
builder.Services.AddSingleton<NetworkService>();
builder.Services.AddSingleton<PortConflictChecker>();
builder.Services.AddSingleton<TagTemplateEngine>();

// Controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
    });

var app = builder.Build();

app.UseCors();
app.UseRouting();
app.MapControllers();
app.MapHub<FleetHub>("/hubs/fleet");

// Serve static files from wwwroot if they exist (SPA)
var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// Initialize services
var fleetService = app.Services.GetRequiredService<FleetService>();
await fleetService.LoadFromDiskAsync();

var urls = builder.Configuration["Urls"] ?? "http://0.0.0.0:5000";
Log.Information("PLC Simulator Supervisor starting on {Urls}", urls);
app.Run();
