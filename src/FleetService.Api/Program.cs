using System.Text.Json.Serialization;
using FleetService.Api.Data;
using FleetService.Api.Endpoints;
using FleetService.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<FleetDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Fleet") ?? "Data Source=fleet.db"));
builder.Services.Configure<OverdueMonitorOptions>(builder.Configuration.GetSection("OverdueMonitor"));
builder.Services.AddHostedService<OverdueMonitor>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<FleetDbContext>().Database.EnsureCreated();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/", () => Results.Redirect("/swagger"));
app.MapFleetEndpoints();
app.Run();

public partial class Program;
