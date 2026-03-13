using Microsoft.Graph;
using SmtpServer;
using System.Security.Cryptography.X509Certificates;

namespace MustMail.MailServer;

public partial class ServerService(
    GraphServiceClient graphClient,
    IConfiguration config,
        ILogger<ServerService> logger, IDbContextFactory<DatabaseContext> dbFactory, ILoggerFactory loggerFactory, UpdateService updates, GraphUserHelper graphUserHelper) : BackgroundService
{
    private SmtpServer.SmtpServer? _smtpServer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        LogSmtpInitializing();

        Configuration mustMailConfig = config.Get<Configuration>()!; // Already checked for null earlier

        LogLoadingCertificate(mustMailConfig.Certificate.Path!);
        X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
            mustMailConfig.Certificate.Path!, // Already checked for null earlier
            Environment.GetEnvironmentVariable("Certificate__Password"));

        // SMTP Server options
        SmtpServerOptionsBuilder smtpBuilder = new SmtpServerOptionsBuilder()
         .ServerName(mustMailConfig.Smtp.Host)
         .Endpoint(builder => builder
             .Port(mustMailConfig.Smtp.ImplicitTLSPort)
             .IsSecure(true)
             .AllowUnsecureAuthentication(false)
             .AuthenticationRequired()
             .Certificate(certificate))
         .Endpoint(builder => builder
             .Port(mustMailConfig.Smtp.StartTLSPort)
             .AllowUnsecureAuthentication(false)
             .AuthenticationRequired()
             .Certificate(certificate));

        if (mustMailConfig.Smtp.AllowInsecure)
        {
            _ = smtpBuilder.Endpoint(builder => builder
                .Port(mustMailConfig.Smtp.InsecurePort)
                .IsSecure(false));
        }

        ISmtpServerOptions smtpOptions = smtpBuilder.Build();

        // Service provider for SmtpServer pipeline
        SmtpServer.ComponentModel.ServiceProvider emailServiceProvider = new();

        LogRegisteringHandlers();

        // Register message handler
        emailServiceProvider.Add(new MessageHandler(
            loggerFactory.CreateLogger<MessageHandler>(),
            graphClient,
            dbFactory,
            mustMailConfig.MustMail,
            updates, 
            graphUserHelper
        ));

        // Register user authenticator 
        emailServiceProvider.Add(new UserAuthenticator(loggerFactory.CreateLogger<UserAuthenticator>(), dbFactory));

        _smtpServer = new SmtpServer.SmtpServer(smtpOptions, emailServiceProvider);

        List<int> ports =
        [
            mustMailConfig.Smtp.ImplicitTLSPort,
            mustMailConfig.Smtp.StartTLSPort
        ];

        if (mustMailConfig.Smtp.AllowInsecure)
        {
            ports.Add(mustMailConfig.Smtp.InsecurePort);
        }

        LogSmtpStarted(mustMailConfig.Smtp.Host, ports);
        
        await _smtpServer.StartAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogSmtpStopping();
        
        await base.StopAsync(cancellationToken);

        LogSmtpStopped();
    }

    // 1100s = ServerService
    [LoggerMessage(
    EventId = 1001,
    Level = LogLevel.Information,
    Message = "Initializing SMTP server")]
    private partial void LogSmtpInitializing();

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Debug,
        Message = "Loading TLS certificate from {Path}")]
    private partial void LogLoadingCertificate(string path);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Debug,
        Message = "Registering SMTP pipeline handlers")]
    private partial void LogRegisteringHandlers();

    [LoggerMessage(
    EventId = 1005,
    Level = LogLevel.Information,
    Message = "SMTP server started on {Host} (ports: {Ports})")]
    private partial void LogSmtpStarted(string host, IEnumerable<int> ports);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "Stopping SMTP server")]
    private partial void LogSmtpStopping();

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Information,
        Message = "SMTP server stopped")]
    private partial void LogSmtpStopped();
}
