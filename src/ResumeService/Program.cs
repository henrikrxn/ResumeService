using Microsoft.AspNetCore.HttpLogging;
using ResumeService;
using ResumeService.Plumbing;
using Serilog;
using Serilog.Events;
using System.Diagnostics;

// This enables ASP.NET Core HTTP integration tests to set-up Serilog so that tests gets logging
if (Log.Logger == Serilog.Core.Logger.None)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .Enrich.WithEnvironmentName() // This only looks at the environment variables, which is not good for e.g. HTTP integration tests
        .Enrich.WithMachineName()
        .Enrich.WithProcessId()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Properties:j}{NewLine}{Exception}")
        .CreateBootstrapLogger();
}
else
{
    Log.Information("Logger already set-up. Skipping Bootstrap logger");
}

try
{
    Log.Information("Creating WebApplication builder");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    Log.Information("Environment: {Environment}", builder.Environment.EnvironmentName);

    if (builder.Environment.IsDevelopment())
    {
        _ = builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true, reloadOnChange: true);
    }

    // Logging in general

    // Clearing the pre-registered providers so we know exactly what has been setup
    _ = builder.Logging.ClearProviders();

    // New ASP.NET Core 8 HTTP logging
    _ = builder.Services.AddHttpLogging(logging =>
    {
        logging.LoggingFields = HttpLoggingFields.All;
        logging.CombineLogs = true;
    });

    // Serilog internal debug logging
    if (builder.Environment.IsDevelopment())
    {
        Log.Information("Setting up Serilog debug logging for development");
        Serilog.Debugging.SelfLog.Enable(msg => Debug.WriteLine(msg));
        Serilog.Debugging.SelfLog.Enable(Console.Error);
    }

    _ = builder.Host.UseSerilog((context, services, configuration) =>
    {
        _ = configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.WithProperty(SerilogProperties.EnvironmentName, context.HostingEnvironment.EnvironmentName)
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.FromLogContext();

        if (context.HostingEnvironment.IsProduction())
        {
            Log.Information("Setting up Serilog for production");
            // If console is deemed in Azure environments there it is probably a good idea to add
            // https://nuget.org/packages/serilog.sinks.async
            // as console historically has been known to slow things down a lot
            _ = configuration.WriteTo.Console(outputTemplate: SerilogTemplates.IncludesProperties, restrictedToMinimumLevel: LogEventLevel.Error); // Console is terribly ineffective, so limiting to the really bad stuff
        }
        else
        {
            Log.Information("Setting up Serilog for Environment: '{Environment}'", context.HostingEnvironment.EnvironmentName);

            _ = configuration.WriteTo.Console(outputTemplate: SerilogTemplates.IncludesProperties);
        }
    }, writeToProviders: !builder.Environment.IsEnvironment(MyAdditionalEnvironments.HttpIntegrationTest));

    // Add services to the container.
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    _ = builder.Services.AddEndpointsApiExplorer();
    _ = builder.Services.AddSwaggerGen();

    Log.Information("Building application");

    using WebApplication app = builder.Build();

    Log.Information("Environment: {Environment}", builder.Environment.EnvironmentName);

    Log.Information("Adding middleware");

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        _ = app.UseSwagger();
        _ = app.UseSwaggerUI();
    }

    _ = app.UseHttpsRedirection();

    // The normal Microsoft request logging
    _ = app.UseHttpLogging();

    string[] summaries = [ "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" ];

    _ = app.MapGet("/weatherforecast", () =>
    {
        WeatherForecast[] forecast = Enumerable.Range(1, 5).Select(index =>
            new WeatherForecast
            (
                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                Random.Shared.Next(-20, 55),
                summaries[Random.Shared.Next(summaries.Length)]
            ))
            .ToArray();
        return forecast;
    })
    .WithName("GetWeatherForecast")
    .WithOpenApi();

    Log.Information("Starting application");

    app.Run();

    Log.Information("Application stopped normally");
    return ExitCodes.Ok;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled problems during application setup and startup");
    return ExitCodes.Error;
}
finally
{
    Log.Information("Flushing and closing Serilog");
    Log.CloseAndFlush();
}

internal sealed record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

// Expose the Program class so that WebApplicationFactory<T> can access it
public partial class Program { }
