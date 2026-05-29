using Microsoft.Extensions.Options;
using Microsoft.Graph;
using MustMail.App.Services.MailProcessing;
using Org.BouncyCastle.Asn1.X509;
using SmtpServer;
using System.Security.Cryptography.X509Certificates;

namespace MustMail.App.Services.Server;

public partial class ServerService(
    GraphServiceClient graphClient,
    IOptionsMonitor<Configuration> config,
    ILogger<ServerService> logger, IDbContextFactory<DatabaseContext> dbFactory, ILoggerFactory loggerFactory, RecipientResolver recipientResolver, SenderResolver senderResolver, AttachmentHandler attachmentHandler, MessageStorage messageStorage) : BackgroundService
{
    private SmtpServer.SmtpServer? _smtpServer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {

        LogSmtpInitializing();

        X509Certificate2 certificate;
        if (config.CurrentValue.Certificate.Format == "PFX")
        {
            LogLoadingCertificate(config.CurrentValue.Certificate.PFXPath!);
            certificate = X509CertificateLoader.LoadPkcs12FromFile(
                                                                                    config.CurrentValue.Certificate.PFXPath!,// Already checked for null earlier
                                                                                    Environment.GetEnvironmentVariable("Certificate__Password"));
        }
        else if (config.CurrentValue.Certificate.Format == "PEM")
        {
            LogLoadingCertificate(config.CurrentValue.Certificate.PEMCertPath!);
            // Load certificate and private key
            certificate =
                X509Certificate2.CreateFromPemFile(config.CurrentValue.Certificate.PEMCertPath!, config.CurrentValue.Certificate.PEMKeyPath!);
           
        }
        else
        {
            throw new InvalidOperationException(
                "Invalid certificate format specified in configuration. Valid values are 'PFX' and 'PEM'.");
        }
       
        // SMTP Server options
        SmtpServerOptionsBuilder smtpBuilder = new SmtpServerOptionsBuilder()
            .ServerName(config.CurrentValue.Smtp.Host)
            .Endpoint(builder => builder
                          .Port(config.CurrentValue.Smtp.ImplicitTLSPort)
                          .IsSecure(true)
                          .AllowUnsecureAuthentication(false)
                          .AuthenticationRequired()
                          .Certificate(certificate))
            .Endpoint(builder => builder
                          .Port(config.CurrentValue.Smtp.StartTLSPort)
                          .AllowUnsecureAuthentication(false)
                          .AuthenticationRequired()
                          .Certificate(certificate));

        if (config.CurrentValue.Smtp.AllowInsecure)
        {
            _ = smtpBuilder.Endpoint(builder => builder
                                         .Port(config.CurrentValue.Smtp.InsecurePort)
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
                                                    config,
                                                    recipientResolver,
                                                    senderResolver,
                                                    attachmentHandler,
                                                    messageStorage
                                                   ));

        // Register user authenticator 
        emailServiceProvider.Add(new UserAuthenticator(loggerFactory.CreateLogger<UserAuthenticator>(), dbFactory));

        _smtpServer = new SmtpServer.SmtpServer(smtpOptions, emailServiceProvider);

        List<int> ports =
        [
            config.CurrentValue.Smtp.ImplicitTLSPort,
            config.CurrentValue.Smtp.StartTLSPort
        ];

        if (config.CurrentValue.Smtp.AllowInsecure)
        {
            ports.Add(config.CurrentValue.Smtp.InsecurePort);
        }

        LogSmtpStarted(config.CurrentValue.Smtp.Host, ports);

        await _smtpServer.StartAsync(stoppingToken);


    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        LogSmtpStopping();

        await base.StopAsync(cancellationToken);

        LogSmtpStopped();
    }

    // 1000s = ServerService
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