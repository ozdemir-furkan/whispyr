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
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Whispyr.Infrastructure;
using Whispyr.Application;



var builder = WebApplication.CreateBuilder(args);


builder.Services.AddScoped<ISummaryService, SummaryService>();


builder.Services.AddHttpClient<ILlmClient, GeminiClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddScoped<IModerationService, LlmModerationService>();
builder.Services.AddInfrastructure();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ILlmClient, GeminiClient>();

builder.Services.AddHostedService<SummaryWorker>();

builder.Services.AddScoped<IModerationService, SimpleModerationService>();

var defaultRedis = builder.Environment.IsDevelopment() ? "localhost:6379" : "redis:6379";

var redisConn = builder.Configuration.GetConnectionString("Redis") ?? defaultRedis;

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect($"{redisConn},abortConnect=false"));

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

var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!))
        };
    });

builder.Services.AddAuthorization();



builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin().SetIsOriginAllowed(_ => true)
));


var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RateLimitMiddleware>();
app.MapControllers();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapHub<RoomHub>("/hubs/room");

app.Run();
