using AWS.Logger;
using Microsoft.Extensions.Logging.Configuration; // Required for LoggerProviderOptions
using AWS.Logger.AspNetCore; // Required for AddAWSProvider

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Configure AWS Logging
if (!builder.Environment.IsDevelopment()) {
    LoggerProviderOptions.RegisterProviderOptions<AWSLoggerConfig, AWSLoggerProvider>(builder.Services);
    builder.Logging.AddAWSProvider(builder.Configuration.GetAWSLoggingConfigSection());
    builder.WebHost.UseUrls("http://*:8080");
}

builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register HttpClient for calling other services
// Using a named client for better organization and potential future specific configurations
builder.Services.AddHttpClient("CoordinationClient", client =>
{
    // You can set default headers or other configurations here if needed in the future
    // client.BaseAddress = new Uri("some_default_base_address_if_applicable/");
});

// Register HttpClient for SAGA coordination
builder.Services.AddHttpClient("SagaCoordinationClient", client =>
{
    // Specific configurations for SAGA client can be added here
});

var app = builder.Build();

// Configure the HTTP request pipeline.

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

// Basic Health Check Endpoint
// Without this, service will fail
app.MapGet("/Transaction/health", () => Results.Ok(new { status = "Healthy" }));

app.Run();
