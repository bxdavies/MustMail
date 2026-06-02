using System.ComponentModel.DataAnnotations;

namespace MustMail.App.Models;

public class Configuration
{
    public string AllowedHosts { get; set; } = "*";
    public string Urls { get; set; } = "http://0.0.0.0:5000";
    public MicrosoftGraphConfiguration Graph { get; private init; } = new();
    public OpenIdConnectConfiguration OpenIdConnect { get; private init; } = new();
    public SmtpConfiguration Smtp { get; init; } = new();
    public MailConfiguration Mail { get; init; } = new();
    public CertificateConfiguration Certificate { get; init; } = new();
    public SerilogConfiguration Serilog { get; set; } = new();

}

public class MicrosoftGraphConfiguration
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public class OpenIdConnectConfiguration
{
    public string NameClaim { get; set; } = "name";
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
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

public class MailConfiguration
{
    public bool TrustFrom { get; set; }
    public bool StoreMail { get; set; } = true;
    public int RetentionDays { get; set; } = 7;
    public List<string> AllowedSenders { get; set; } = [];
    public List<string> AllowedRecipients { get; set; } = [];
    public bool FooterBranding { get; set; } = true;
}

public class CertificateConfiguration
{
    public bool Managed { get; set; } = true;
    public string? Format { get; set; }
    public string? PFXPath { get; set; }
    public string? Password { get; set; }
    public string? PEMCertPath { get; set; }
    public string? PEMKeyPath { get; set; }
    public string CommonName { get; set; } = "localhost";

}

public class SerilogConfiguration
{
    public List<string> Using { get; set; } = ["Serilog.Sinks.Console"];
    public MinimumLevelConfiguration MinimumLevel { get; init; } = new MinimumLevelConfiguration();
    public List<WriteToConfiguration> WriteTo { get; init; } = [ new WriteToConfiguration() ];
}

public class MinimumLevelConfiguration
{
    public string Default { get; set; } = "Information";
}

public class WriteToConfiguration
{
    public string Name { get; set; } = "Console";
    public Dictionary<string, object> Args { get; set; } = new()
    {
        ["outputTemplate"] =
           "{Timestamp:O} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}"
    };
}