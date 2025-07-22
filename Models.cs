namespace MustMail;

public class Configuration
{
    public required SmtpConfiguration Smtp { get; init; }
    public required MicrosoftGraphConfiguration Graph { get; init; }
    public required string SendFrom { get; init; }
    public string LogLevel { get; init; } = "Information";
}
public class SmtpConfiguration
{
    public required string Host { get; set; }
    public int Port { get; set; }
}

public class MicrosoftGraphConfiguration
{
    public required string TenantId { get; set; }
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
}