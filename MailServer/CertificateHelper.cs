using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MustMail.MailServer;

public static partial class CertificateHelper
{
    public static void Create(Configuration mustMailConfig, ILoggerFactory loggerFactory)
    {
        ILogger logger = loggerFactory.CreateLogger("CertificateHelper");

        LogCertificateCreationStarted(logger, mustMailConfig.Certificate.CommonName);

        // NIST recommends a minimum of 2048-bit keys for RSA
        LogKeyGeneration(logger, 2048);
        using RSA rsa = RSA.Create(2048);

        CertificateRequest req = new($"CN={mustMailConfig.Certificate.CommonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        LogAddingExtensions(logger);

        // Required for modern TLS (hostname validation)
        SubjectAlternativeNameBuilder san = new();
        san.AddDnsName(mustMailConfig.Certificate.CommonName);
        req.CertificateExtensions.Add(san.Build());

        // Mark as server cert (not a CA)
        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));

        // Required for TLS server usage
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));

        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                [new Oid("1.3.6.1.5.5.7.3.1")], // Server Authentication
                true));

        using X509Certificate2 cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(5));

        LogCertificateGenerated(logger, cert.NotAfter);

        File.WriteAllBytes(mustMailConfig.Certificate.Path!, cert.Export(X509ContentType.Pfx, Environment.GetEnvironmentVariable("Certificate__Password")));

        LogCertificateSaved(logger, mustMailConfig.Certificate.Path!);
    }

    [LoggerMessage(
    EventId = 3001,
    Level = LogLevel.Information,
    Message = "Starting creation of self-signed TLS certificate for {CommonName}")]
    private static partial void LogCertificateCreationStarted(ILogger logger, string commonName);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Debug,
        Message = "Generating RSA key with {KeySize}-bit length")]
    private static partial void LogKeyGeneration(ILogger logger, int keySize);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Debug,
        Message = "Adding certificate extensions (SAN, BasicConstraints, KeyUsage, EKU)")]
    private static partial void LogAddingExtensions(ILogger logger);

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Information,
        Message = "Self-signed certificate generated. Valid until {Expiry}")]
    private static partial void LogCertificateGenerated(ILogger logger, DateTimeOffset expiry);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Information,
        Message = "Certificate written to {Path}")]
    private static partial void LogCertificateSaved(ILogger logger, string path);
}