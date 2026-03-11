using System.ComponentModel.DataAnnotations;

namespace MustMail.Models;

public class Configuration
{
    public string AllowedHosts { get; set; } = "*";
    public string Urls { get; set; } = "http://0.0.0.0:5000";
    public required SmtpConfiguration Smtp { get; init; } = new();
    public required OpenIdConnectConfiguration OpenIdConnect { get; init; } = new();
    public required MustMailConfiguration MustMail { get; init; } = new();
    public required CertificateConfiguration Certificate { get; init; } = new();
    public SerilogConfiguration Serilog { get; init; } = new();
   
}
public class SmtpConfiguration
{
    public string Host { get; set; } = "localhost";
    public bool AllowInsecure { get; set; }
    [Range(1, 65535)]
    public int InsecurePort { get; set; } = 25;

    [Range(1, 65535)]
    public int ImplicitTLSPort { get; set; } = 465;
    [Range(1, 65535)]
    public int StartTLSPort { get; set; } = 587;
}


public class OpenIdConnectConfiguration
{
    public string? NameClaim { get; set; }
}

public class MustMailConfiguration
{
    public bool TrustFrom { get; set; }
    public bool StoreEmails { get; set; } = true;
    public int RetentionDays { get; set; } = 7;
    public List<string> AllowedSenders { get; set; } = [];
    public List<string> AllowedRecipients { get; set; } = [];
    public bool FooterBranding { get; set; } = true;
}

public class CertificateConfiguration
{
    public bool Managed { get; set; } = true;
    public string? Path { get; set; }
    public string CommonName { get; set; } = "localhost";

}

public class SerilogConfiguration
{
    public MinimumLevelConfiguration MinimumLevel { get; set; } = new();
}

public class MinimumLevelConfiguration
{
    public string Default { get; set; } = "Information";
}
