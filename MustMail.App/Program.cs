using Azure.Core;
using Azure.Identity;
using DbUp;
using DbUp.Engine;
using Duende.AccessTokenManagement.OpenIdConnect;
using Isopoh.Cryptography.Argon2;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.Graph;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MudBlazor.Extensions;
using MudBlazor.Services;
using MustMail.App;
using MustMail.App.Auth;
using MustMail.App.Components;
using MustMail.App.Services.MailProcessing;
using MustMail.App.Services.Maintenance;
using MustMail.App.Services.Server;
using Quartz;
using Serilog;
using Serilog.Events;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

// Create builder
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add health checks
builder.Services.AddHealthChecks();

// Create the Data folder
string dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");
Directory.CreateDirectory(dataFolder);

string appSettingsPath = Path.Combine(dataFolder, "appsettings.json");

// If appsettings.json does not exist create one with the default config 
if (!File.Exists(appSettingsPath))
{
    File.WriteAllText(
                      appSettingsPath,
                      JsonSerializer.Serialize(new Configuration(), JsonWriteDefaults.Options));
}

// If no sink is set then use the console
if (string.IsNullOrEmpty(builder.Configuration.GetValue<string?>("Serilog:Using:0")))
{
    builder.Configuration["Serilog:Using:0"] = "Serilog.Sinks.Console";

    builder.Configuration["Serilog:MinimumLevel:Default"] = "Information";

    builder.Configuration["Serilog:WriteTo:0:Name"] = "Console";

    builder.Configuration["Serilog:WriteTo:0:Args:outputTemplate"] =
        "{Timestamp:O} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}";

}

// Create logger using default settings, appsettings.json and environment files will override this
LoggerConfiguration loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Is(LogEventLevel.Information)
    // Set minimum levels for noisy log sources 
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.WebTools.BrowserLink.Net.BrowserLinkMiddleware", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Extensions.Localization.ResourceManagerStringLocalizer", LogEventLevel.Information)
    .MinimumLevel.Override("Quartz.Core.QuartzSchedulerThread", LogEventLevel.Information)
    .MinimumLevel.Override("Quartz.Impl.StdSchedulerFactory", LogEventLevel.Warning)
    .MinimumLevel.Override("Quartz.Core.SchedulerSignalerImpl", LogEventLevel.Warning)
    .MinimumLevel.Override("Quartz.Simpl.RAMJobStore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .ReadFrom.Configuration(builder.Configuration);


// Create Serilog logger
Log.Logger = loggerConfig.CreateLogger();

// Create logger factory
using ILoggerFactory loggerFactory = new LoggerFactory().AddSerilog(Log.Logger);

// Set Serilog as the logging provider
builder.Services.AddSerilog();

// Version and copyright message
Log.Information("MustMail");
Log.Information(Assembly.GetEntryAssembly()!.GetName().Version?.ToString(3)!);

// Validate required environment variables
Helpers.ValidateEnvironmentVariables();

Log.Logger.Information("Loaded configuration from {ConfigPath}", Path.Combine(dataFolder, "appsettings.json"));

// Store managed certificates in the data directory
if (builder.Configuration.GetValue<bool?>("Certificate:Managed") != false)
{
    string certPath = Path.Combine(dataFolder, "MustMail.pfx");
    bool exists = File.Exists(certPath);

    if (!exists)
    {
        Log.Logger.Information(
                               "Managed certificates enabled. A new certificate will be created at {CertificatePath}",
                               certPath);
    }
    builder.Configuration["Certificate:Path"] = certPath;

}

// If database connection string is not set store MustMail.db in the data folder. 
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Sqlite")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__MySQL")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer")) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__AzureSql")))
{
    string databasePath = Path.Combine(dataFolder, "MustMail.db");
    bool exists = File.Exists(databasePath);

    if (exists)
    {
        Log.Logger.Information(
                               "Database connection string not set. Defaulting to existing SQLite database at {DatabasePath}",
                               databasePath);

    }
    else
    {
        Log.Logger.Information(
                               "Database connection string not set. A new SQLite database will be created at {DatabasePath}",
                               databasePath);
    }

    Environment.SetEnvironmentVariable("ConnectionStrings__Sqlite", $"Data Source={databasePath}");
}

// If name claim is not set use the default name
if (string.IsNullOrWhiteSpace(builder.Configuration["OpenIdConnect:NameClaim"]))
{
    builder.Configuration["OpenIdConnect:NameClaim"] = "name";

}

// Parse configuration
Configuration mustMailConfig = builder.Configuration.Get<Configuration>()
                               ?? throw new InvalidOperationException(
                                                                      "Could not load MustMail configuration. Please see the README for configuration guidance.");


if (mustMailConfig.Smtp.InsecurePort is < 1 or > 65535)
    throw new InvalidOperationException("Smtp:InsecurePort must be between 1 and 65535.");

if (mustMailConfig.Smtp.ImplicitTLSPort is < 1 or > 65535)
    throw new InvalidOperationException("Smtp:ImplicitTLSPort must be between 1 and 65535.");

if (mustMailConfig.Smtp.StartTLSPort is < 1 or > 65535)
    throw new InvalidOperationException("Smtp:StartTLSPort must be between 1 and 65535.");

Log.Logger.Information(
                       "All persistent data is stored in '{DataFolder}'. Ensure this directory is mounted as a Docker volume to avoid data loss.",
                       dataFolder);

// Log configuration
Log.Information("Configuration: \n {Serialize}", JsonSerializer.Serialize(mustMailConfig, JsonDefaults.Options));

// Create DbUp upgrader for database migrations 
UpgradeEngine upgrader;

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Sqlite")))
{
    // Database connection
    builder.Services.AddDbContextFactory<DatabaseContext>(options => options.UseSqlite(Environment.GetEnvironmentVariable("ConnectionStrings__Sqlite")));

    // Initialize DbUp upgrader to use Sqlite
    upgrader =
        DeployChanges.To
            .SqliteDatabase(Environment.GetEnvironmentVariable("ConnectionStrings__Sqlite"))
            .WithScriptsFromFileSystem(Path.Combine(AppContext.BaseDirectory, "Db", "Scripts", "Sqlite"))
            .LogTo(loggerFactory)
            .Build();

}
else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")))
{
    builder.Services.AddDbContextFactory<DatabaseContext>(options => options.UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")));

    // Initialize DbUp upgrader to use Postgres
    upgrader =
        DeployChanges.To
            .PostgresqlDatabase(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres"))
            .WithScriptsFromFileSystem(Path.Combine(AppContext.BaseDirectory, "Db", "Scripts", "Postgres"))
            .LogTo(loggerFactory)
            .Build();
}
else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__MySQL")))
{
    builder.Services.AddDbContextFactory<DatabaseContext>(options => options.UseMySQL(Environment.GetEnvironmentVariable("ConnectionStrings__MySQL")!));

    // Initialize DbUp upgrader to use MySql
    upgrader =
        DeployChanges.To
            .MySqlDatabase(Environment.GetEnvironmentVariable("ConnectionStrings__MySQL"))
            .WithScriptsFromFileSystem(Path.Combine(AppContext.BaseDirectory, "Db", "Scripts", "MySQL"))
            .LogTo(loggerFactory)
            .Build();
}
else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer")))
{
    builder.Services.AddDbContextFactory<DatabaseContext>(options => options.UseSqlServer(Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer")));

    // Initialize DbUp upgrader to use SqlServer
    upgrader =
        DeployChanges.To
            .SqlDatabase(Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer"))
            .WithScriptsFromFileSystem(Path.Combine(AppContext.BaseDirectory, "Db", "Scripts", "SqlServer"))
            .LogTo(loggerFactory)
            .Build();
}
else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__AzureSql")))
{
    builder.Services.AddDbContextFactory<DatabaseContext>(options => options.UseAzureSql(Environment.GetEnvironmentVariable("ConnectionStrings__AzureSql")));

    // Initialize DbUp upgrader to use AzureSql
    upgrader =
        DeployChanges.To
            .AzureSqlDatabaseWithIntegratedSecurity(Environment.GetEnvironmentVariable("ConnectionStrings__AzureSql"))
            .WithScriptsFromFileSystem(Path.Combine(AppContext.BaseDirectory, "Db", "Scripts", "AzureSql"))
            .LogTo(loggerFactory)
            .Build();
}
else
{
    throw new InvalidOperationException("No valid database connection string found!");
}

// If running in development capture ef core migration issues
if (builder.Environment.IsDevelopment())
    _ = builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Run database migrations 
DatabaseUpgradeResult result = upgrader.PerformUpgrade();

// If there is an error log it and throw an expectation 
if (!result.Successful)
{
    Log.Error(result.Error, "Database migration failed.");
    throw new InvalidOperationException("Database migration failed!");
}

// Create client secret credential to authenticate against Microsoft graph
builder.Services.AddSingleton<TokenCredential>(_ => new ClientSecretCredential(
                                                                               Environment.GetEnvironmentVariable("Graph__TenantId"),
                                                                               Environment.GetEnvironmentVariable("Graph__ClientId"),
                                                                               Environment.GetEnvironmentVariable("Graph__ClientSecret"),
                                                                               new ClientSecretCredentialOptions
                                                                               {
                                                                                   AuthorityHost = AzureAuthorityHosts.AzurePublicCloud
                                                                               }
                                                                              ));

// Create the Microsoft graph client
builder.Services.AddSingleton(sp => {
    TokenCredential credential = sp.GetRequiredService<TokenCredential>();
    return new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
});

// Add forward headers for reverse proxy
builder.Services.Configure<ForwardedHeadersOptions>(options => {
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add ODIC Authentication
builder.Services.AddAuthentication(options => {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddOpenIdConnect(options => {
        options.Authority = Environment.GetEnvironmentVariable("OpenIdConnect__Authority");
        options.ClientId = Environment.GetEnvironmentVariable("OpenIdConnect__ClientId");
        options.ClientSecret = Environment.GetEnvironmentVariable("OpenIdConnect__ClientSecret");
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.TokenValidationParameters.NameClaimType = mustMailConfig.OpenIdConnect.NameClaim;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = OpenIdConnectHandlers.OnTokenValidated
        };
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

// Non-interactive token refresh using Duende Access Token Management
builder.Services.AddOpenIdConnectAccessTokenManagement();

// Create authorization policy for admin page
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("MustMailAdmin", p =>
                   p.RequireClaim("must_mail_role", "admin"));

// Add Cascading Authentication State
builder.Services.AddCascadingAuthenticationState();

// Add update service
builder.Services.AddSingleton<UpdateService>();

string? certificatePath = mustMailConfig.Certificate.Path;

if (string.IsNullOrWhiteSpace(certificatePath))
{
    throw new InvalidOperationException(
                                        "Certificate path is not configured and MustMail is not managing the certificate.");
}

if (!File.Exists(certificatePath) && mustMailConfig.Certificate.Managed)
{
    Log.Information(
                    "Managed certificate not found at {CertificatePath}. Creating a new self-signed certificate.",
                    certificatePath);

    CertificateGenerator.Create(mustMailConfig, loggerFactory);
}

if (!File.Exists(certificatePath) && !mustMailConfig.Certificate.Managed)
{
    throw new InvalidOperationException(
                                        $"Could not find or access certificate at '{certificatePath}'.");
}

// Attempt to load certificate and check it's valid
X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
                                                                        mustMailConfig.Certificate.Path!,
                                                                        Environment.GetEnvironmentVariable("Certificate__Password"));

// If certificate has expired create a new one if managed else throw an exception
if (certificate.NotAfter <= DateTime.UtcNow)
{
    if (mustMailConfig.Certificate.Managed)
    {
        CertificateGenerator.Create(mustMailConfig, loggerFactory);
    }
    else
    {
        throw new InvalidOperationException($"The certificate at '{mustMailConfig.Certificate.Path}' expired on '{certificate.NotBefore.ToIsoDateString()}'. Please renew and replace the certificate.");
    }
}

// Create the SMTP server
builder.Services.AddHostedService<ServerService>();

// Add graph user helper for finding users in M365 by UPN, Mail or aliais address 
builder.Services.AddSingleton<GraphUserLookupService>();

// Add recipient and sender resolvers which fetch the recipients and sender from the message and checks they are allowed to send
builder.Services.AddSingleton<RecipientResolver>();
builder.Services.AddSingleton<SenderResolver>();

// Add attachment handler for extracting attachments from the message and then reattaching them using graph
builder.Services.AddSingleton<AttachmentHandler>();

// Add message storage for storing messages on disk and in the DB if message storage is enabled
builder.Services.AddSingleton<MessageStorage>();

// If we are storing emails create the cleanup job
if (mustMailConfig.MustMail.StoreEmails)
{
    _ = builder.Services.AddQuartz(q => {
        // Run job now
        _ = q.ScheduleJob<CleanupService>(trigger => trigger
                                              .WithIdentity("Cleanup Job Immediate")
                                              .StartNow()
                                         );

        // Run job on the hour every hour
        _ = q.ScheduleJob<CleanupService>(trigger => trigger
                                              .WithIdentity("Cleanup Job Hourly")
                                              .WithCronSchedule("0 0 * * * ?")
                                              .WithDescription("Remove any emails older than the retention configuration every hour")
                                         );
    });

    _ = builder.Services.AddQuartzHostedService(options => {
        // when shutting down we want jobs to complete gracefully
        options.WaitForJobsToComplete = true;
    });

}

// Add MudBlazor services
builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Healthcheck for database
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DatabaseContext>();

// Build the app
WebApplication app = builder.Build();

// Create a health check endpoint at /healthz
app.MapHealthChecks("/healthz");

// Bootstrap SMTP Accounts
using (IServiceScope scope = app.Services.CreateScope())
{
    DatabaseContext dbContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

    string? env = Environment.GetEnvironmentVariable("Bootstrap__SMTPAccounts");

    if (!string.IsNullOrWhiteSpace(env))
    {
        foreach (string account in env.Split('|', StringSplitOptions.RemoveEmptyEntries))// Split multiple accounts
        {
            // Split username:password
            string[] parts = account.Split(':', 2);
            if (parts.Length != 2) continue;

            string username = parts[0];
            string password = parts[1];

            if (!dbContext.SMTPAccount.Any(u => u.Username == username))
            {
                // Create account
                dbContext.SMTPAccount.Add(new SMTPAccount
                {
                    Username = username,
                    Password = Argon2.Hash(password),
                    Description = $"Account {username} bootstrapped from environment variable"
                });

                Log.Debug("Added bootstrap SMTP account {Username}", username);

                await dbContext.SaveChangesAsync();
            }

        }


    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    _ = app.UseExceptionHandler("/Error", true);
    _ = app.UseMigrationsEndPoint();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Map login and logout endpoints
app.MapGroup("/authentication").MapLoginAndLogout();

// Map static folder maildrop if we are storing emails
if (mustMailConfig.MustMail.StoreEmails)
{
    // Create maildrop folder
    string maildropFolder = Path.Combine(dataFolder, "maildrop");
    _ = Directory.CreateDirectory(maildropFolder);

    if (app.Logger.IsEnabled(LogLevel.Information))
        app.Logger.LogInformation("Using maildrop folder in data directory: {MaildropPath}", maildropFolder);

    // Create a custom static path at /maildrop for eml files and attachments 
    _ = app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(maildropFolder),
        RequestPath = "/maildrop",
        OnPrepareResponse = MaildropStaticFileAuth.OnPrepareResponse
    });
}

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();