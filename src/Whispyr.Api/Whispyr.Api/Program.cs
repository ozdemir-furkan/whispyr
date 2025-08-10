using OpenTelemetry.Exporter;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Serilog;
using Whispyr.Infrastructure.Data;
using Whispyr.Api.Hubs;
using OpenTelemetry.Instrumentation.EntityFrameworkCore;
using OpenTelemetry.Instrumentation.Runtime;
using OpenTelemetry.Instrumentation.Process;
using StackExchange.Redis;
using Whispyr.Api.Middleware;
using Whispyr.Application.Abstractions;
using Whispyr.Infrastructure.Services;
using Whispyr.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<SummaryWorker>();

builder.Services.AddScoped<IModerationService, SimpleModerationService>();

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect("localhost:6379"));

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddOtlpExporter());

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSignalR();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin().SetIsOriginAllowed(_ => true)
));

builder.Services.AddControllers();

var app = builder.Build();
app.UseCors();
app.UseMiddleware<RateLimitMiddleware>();
app.MapControllers();


app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapHub<RoomHub>("/hubs/room");

app.Run();
