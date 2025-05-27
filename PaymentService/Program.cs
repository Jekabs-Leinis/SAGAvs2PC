using AWS.Logger;
using Microsoft.EntityFrameworkCore;
using PaymentService.Data;
using Microsoft.Extensions.Logging.Configuration;
using AWS.Logger.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure AWS Logging
if (!builder.Environment.IsDevelopment()) {
    LoggerProviderOptions.RegisterProviderOptions<AWSLoggerConfig, AWSLoggerProvider>(builder.Services);
    builder.Logging.AddAWSProvider(builder.Configuration.GetAWSLoggingConfigSection());
    builder.WebHost.UseUrls("http://*:8080");
}

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure DbContext
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

// Basic Health Check Endpoint
// Without this, service will fail
app.MapGet("/Payments/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();
