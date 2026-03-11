using Azure.Core;
using Azure.Identity;
using DbUp;
using DbUp.Engine;
using Duende.AccessTokenManagement.OpenIdConnect;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Microsoft.Graph;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MudBlazor.Extensions;
using MudBlazor.Services;
using MustMail;
using MustMail.Auth;
using MustMail.Components;
using MustMail.MailServer;
using Quartz;
using Serilog;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

// Create builder
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Create the Data folder
string dataFolder = Path.Combine(AppContext.BaseDirectory, "Data");
Directory.CreateDirectory(dataFolder);

// Load the  configuration
builder.Configuration
    .SetBasePath(dataFolder)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

// Create Serilog logger
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(Serilog.Events.LogEventLevel.Information)
    .WriteTo.Console(
        theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Literate,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level} {SourceContext}] {Message:lj}{NewLine}{Exception}")
    // Set minimum levels for noisy log sources 
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", Serilog.Events.LogEventLevel.Warning) // Request logging
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning) // Everything AspNetCore logging
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Information) // Entity Framework logging
    .MinimumLevel.Override("Microsoft.WebTools.BrowserLink.Net.BrowserLinkMiddleware", Serilog.Events.LogEventLevel.Warning) // Browser link logging (used in development)  
    .MinimumLevel.Override("Microsoft.Extensions.Localization.ResourceManagerStringLocalizer", Serilog.Events.LogEventLevel.Information) // Localization logging
    .MinimumLevel.Override("Quartz.Core.QuartzSchedulerThread", Serilog.Events.LogEventLevel.Information) // Quartz Thread logging
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

// Create logger factory
using ILoggerFactory loggerFactory = new LoggerFactory().AddSerilog(Log.Logger, dispose: false);

// Set Serilog as the logging provider
builder.Services.AddSerilog();

// Version and copyright message
Log.Information("MustMail");
Log.Information(Assembly.GetEntryAssembly()!.GetName().Version?.ToString(3)!);

// Validate required environment variables
Helpers.ValidateEnvironmentVariables();

Log.Logger.Information("Loaded configuration from {ConfigPath}", Path.Combine(dataFolder, "appsettings.json"));

bool configChanged = false;

// Store managed certificates in the data directory
if (builder.Configuration.GetValue<bool?>("Certificate:Managed") != false)
{
    string certPath = Path.Combine(dataFolder, "MustMail.pfx");
    bool exists = File.Exists(certPath);

    if (exists)
    {
        Log.Logger.Information(
          "Managed certificates enabled. Using existing certificate at {CertificatePath}",
          certPath);
    }
    else
    {
        configChanged = true;
        Log.Logger.Information(
          "Managed certificates enabled. A new certificate will be created at {CertificatePath}",
          certPath);
    }
    builder.Configuration["Certificate:Path"] = certPath;

}

// If database connection string is not set store MustMail.db in the data folder. 
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ConnectionStrings__DatabaseContext")))
{
    string databasePath = Path.Combine(dataFolder, "MustMail.db");
    bool exists = File.Exists(databasePath);

    if (exists)
    {
        Log.Logger.Information(
          "Database connection string not set. Using existing database at {DatabasePath}",
         databasePath);

    }
    else
    {
        configChanged = true;
        Log.Logger.Information(
          "Database connection string not set. A new database will be created at {DatabasePath}",
        databasePath);
    }

    Environment.SetEnvironmentVariable("ConnectionStrings__DatabaseContext", $"Data Source={databasePath}");
}
else
{
    Log.Logger.Information("Using configured database connection string from environment variable.");
}

// If name claim is not set use the default name
if (string.IsNullOrWhiteSpace(builder.Configuration["OpenIdConnect:NameClaim"]))
{
    builder.Configuration["OpenIdConnect:NameClaim"] = "name";
    configChanged = true;

    Log.Logger.Information(
        "OpenID Connect name claim not configured. Defaulting to {NameClaim}",
        "name");
}

// Parse configuration
Configuration mustMailConfig = builder.Configuration.Get<Configuration>()
    ?? throw new InvalidOperationException(
        "Could not load MustMail configuration. Please see the README for configuration guidance.");

// If we have overridden the configuration then save it and log
if (configChanged)
{
    string appSettingsPath = Path.Combine(dataFolder, "appsettings.json");

    File.WriteAllText(
        appSettingsPath,
        JsonSerializer.Serialize(mustMailConfig, JsonDefaults.Options));

    Log.Logger.Information(
        "Configuration defaults were applied and written to {ConfigPath}",
        appSettingsPath);
}

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

// Database connection
builder.Services.AddDbContextFactory<DatabaseContext>(options =>
    options.UseSqlite(Environment.GetEnvironmentVariable("ConnectionStrings__DatabaseContext")));

//EnsureDatabase.For(builder.Configuration.GetConnectionString("DatabaseContext"));

// If running in development capture ef core migration issues
if (builder.Environment.IsDevelopment())
    _ = builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Create DbUp upgrader for database migrations 
UpgradeEngine upgrader =
        DeployChanges.To
            .SqliteDatabase(Environment.GetEnvironmentVariable("ConnectionStrings__DatabaseContext"))
            .WithScriptsFromFileSystem(Path.Combine(AppContext.BaseDirectory, "Db", "Scripts"))
            .LogTo(loggerFactory)
            .Build();

// Run database migrations 
DatabaseUpgradeResult result = upgrader.PerformUpgrade();

// If there is an error log it and throw an expectation 
if (!result.Successful)
{
    Log.Error(result.Error, "Database migration failed.");
    throw new InvalidOperationException("Database migration failed!");
}

Log.Information("Database migration completed successfully.");

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
builder.Services.AddSingleton(sp =>
{
    TokenCredential credential = sp.GetRequiredService<TokenCredential>();
    return new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
});

// Add forward headers for reverse proxy
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add ODIC Authentication
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddOpenIdConnect(options =>
    {
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

if (File.Exists(certificatePath))
{
    Log.Information(
        "Using certificate at {CertificatePath}",
        certificatePath);
}
else if (mustMailConfig.Certificate.Managed)
{
    Log.Information(
        "Managed certificate not found at {CertificatePath}. Creating a new self-signed certificate.",
        certificatePath);

    CertificateHelper.Create(mustMailConfig, loggerFactory);
}
else
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
        CertificateHelper.Create(mustMailConfig, loggerFactory);
    }
    else
    {
        throw new InvalidOperationException($"The certificate at '{mustMailConfig.Certificate.Path}' expired on '{certificate.NotBefore.ToIsoDateString()}'. Please renew and replace the certificate.");
    }
}

// Create the SMTP server
builder.Services.AddHostedService<ServerService>();

// Add graph user helper for finding users in M365 by UPN, Mail or aliais address 
builder.Services.AddSingleton<GraphUserHelper>();

// If we are storing emails create the cleanup job
if (mustMailConfig.MustMail.StoreEmails)
{
    _ = builder.Services.AddQuartz(q =>
    {
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

    _ = builder.Services.AddQuartzHostedService(options =>
    {
        // when shutting down we want jobs to complete gracefully
        options.WaitForJobsToComplete = true;
    });

}

// Add MudBlazor services
builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Build the app
WebApplication app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    _ = app.UseExceptionHandler("/Error", createScopeForErrors: true);
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
        app.Logger.LogInformation("Using the 'folder' maildrop in the data directory. Full path: {MaildropPath}", maildropFolder);

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

