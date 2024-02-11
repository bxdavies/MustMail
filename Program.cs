using System.Reflection;
using System.Text.Json;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using MustMail;
using SmtpServer;
using ServiceProvider = SmtpServer.ComponentModel.ServiceProvider;

// Version and copyright message
Console.ForegroundColor = ConsoleColor.Cyan; 
Console.WriteLine("Must Mail");
Console.WriteLine(Assembly.GetEntryAssembly()!.GetName().Version?.ToString(3));
Console.ForegroundColor = ConsoleColor.White;

// Reading the environment variable for log level
string logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL") ?? "Information";

// Setting log level based on the environment variable
LogLevel logLevelEnum = LogLevel.Information;
if (Enum.TryParse(logLevel, true, out LogLevel parsedLogLevel))
{
    logLevelEnum = parsedLogLevel;
}

// Creating logger factory
ILoggerFactory factory = LoggerFactory.Create(builder =>
{
    builder.AddConsole().AddFilter((_, level) => level >= logLevelEnum);
});

ILogger logger = factory.CreateLogger("MustMail");

// Configuration
IConfigurationBuilder builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

IConfiguration configuration = builder.Build();

Configuration? config  = configuration.Get<Configuration>();

// If configuration can not be parsed to config - exit
if (config == null)
{
    logger.LogError("Could not load the configuration! Please see the README for how to set the configuration!");
    Environment.Exit(1);
}

// Log configuration
logger.LogInformation("Configuration: \n {Serialize}", JsonSerializer.Serialize(config, new JsonSerializerOptions{WriteIndented = true}));

// Create SMTP Server options
ISmtpServerOptions? options = new SmtpServerOptionsBuilder()
    .ServerName(config.Smtp.Host)
    .Port(config.Smtp.Port, false)
    .Build();

// Create client secrete credential 
ClientSecretCredential clientSecretCredential = new(
    config.Graph.TenantId, 
    config.Graph.ClientId, 
    config.Graph.ClientSecret,
    new ClientSecretCredentialOptions
    {
        AuthorityHost =  AzureAuthorityHosts.AzurePublicCloud
    }
);

// Create graph client
GraphServiceClient graphClient = new(clientSecretCredential, new[] { "https://graph.microsoft.com/.default" });

// Create email service provider
ServiceProvider emailServiceProvider = new();

// Add the message handler to the service provider
emailServiceProvider.Add(new MessageHandler(graphClient, logger, config.SendFrom));

// Create the server
SmtpServer.SmtpServer smtpServer = new(options, emailServiceProvider);

// Log server start
logger.LogInformation("Smtp server started on {SmtpHost}:{SmtpPort}", config.Smtp.Host, config.Smtp.Port);

// Start the server
await smtpServer.StartAsync(CancellationToken.None);


