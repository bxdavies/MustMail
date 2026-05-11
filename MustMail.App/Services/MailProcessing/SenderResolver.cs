using MimeKit;
using SmtpServer.Protocol;

namespace MustMail.App.Services.MailProcessing;

public partial class SenderResolver(ILogger<SenderResolver> logger, GraphUserLookupService graphUserHelper, IConfiguration config)
{
    private readonly MustMailConfiguration _mustMailConfig = config.Get<Configuration>()!.MustMail;

    public async Task<ResolvedSender> ResolveSender(SmtpServer.IMessageTransaction transaction, MimeMessage message)
    {

        // Try MAIL FROM first (preferred)
        string? senderAddress =
            transaction.From == null ||
            string.IsNullOrWhiteSpace(transaction.From.User) ||
            string.IsNullOrWhiteSpace(transaction.From.Host)
                ? null
                : $"{transaction.From.User.Trim()}@{transaction.From.Host.Trim()}";
        string? senderName = message.Sender?.Name?.Trim();

        string type = "MAIL FROM";
   

        // MAIL FROM was not found falling back to FROM
        if (string.IsNullOrWhiteSpace(senderAddress))
        {
            // Check if we trust the FROM address
            if (!_mustMailConfig.TrustFrom)
            {
                LogMailFromMissingTrustFromDisabled();
                return new ResolvedSender
                {
                    SmtpResponse = SmtpResponse.SyntaxError
                };
            }

            // Get first FROM Header Field
            senderAddress = message.From.OfType<MailboxAddress>()
                .Select(a => a.Address)
                .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

            senderName = message.From.OfType<MailboxAddress>()
                .Select(a => a.Name)
                .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

            // Check a FROM address was found
            if (string.IsNullOrWhiteSpace(senderAddress))
            {
                LogFromHeaderMissing();
                return new ResolvedSender
                {
                    SmtpResponse = SmtpResponse.MailboxUnavailable
                };
            }

            LogUsingFromFallback(senderAddress);

            type = "FROM";
        }

        // If allowedFrom is not wildcard and there are allowed recipients in the list then loop through each one
        if (!_mustMailConfig.AllowedSenders.Contains("*") && _mustMailConfig.AllowedSenders.Count > 0)
        {

            // Check if address is allowed
            bool isAllowed = _mustMailConfig.AllowedSenders.Any(allowed => {

                // Direct comparison of address eg. user@example.com compared to user@example.com
                if (string.Equals(
                                  allowed,
                                  senderAddress,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Comparison of domain eg. example.com compared to example.com
                if (allowed.StartsWith("*@"))
                {
                    return senderAddress.EndsWith(
                                                     allowed[1..], // This extracts only the domain 
                                                     StringComparison.OrdinalIgnoreCase);
                }

                return false;
            });

            // If the address is not allowed add then return an error
            if (!isAllowed)
            {
                LogSenderRejected(senderAddress);
                return new ResolvedSender
                {
                    SmtpResponse = SmtpResponse.SyntaxError
                };
            }
        }


        // Attempt to get the user from graph by UPN, Mail or any alias addresses
        Microsoft.Graph.Models.User? user;
        try
        {
            user = await graphUserHelper.FindSenderUserAsync(
                                                             type,
                                                             senderAddress);

            if (user == null)
            {
                return new ResolvedSender
                {
                    SmtpResponse = SmtpResponse.MailboxUnavailable
                };
            }
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError error)
        {
            LogSenderTenantLookupFailed(error, type, senderAddress);
            return new ResolvedSender
            {
                SmtpResponse = SmtpResponse.SyntaxError
            };
        }

        // If no sender name is provided fall back to the sender address as the sender name
        if (string.IsNullOrWhiteSpace(senderName))
        {
            senderName = senderAddress;
        }

        return new ResolvedSender
        {
            Name = senderName,
            Address = senderAddress,
            User = user
        };
    }
    
    // 1120s = SenderResolver
    
    [LoggerMessage(EventId = 1120, Level = LogLevel.Information, Message = "Falling back to From header sender {Sender}")]
    private partial void LogUsingFromFallback(string sender);

    [LoggerMessage(EventId = 1121, Level = LogLevel.Warning, Message = "MAIL FROM missing and TrustFrom is disabled. Message rejected")]
    private partial void LogMailFromMissingTrustFromDisabled();

    [LoggerMessage(EventId = 1122, Level = LogLevel.Warning, Message = "MAIL FROM missing and no usable From header address was found. Message rejected")]
    private partial void LogFromHeaderMissing();

    [LoggerMessage(EventId = 1123, Level = LogLevel.Warning, Message = "Sender {Sender} rejected because it is not in the allowed sender list")]
    private partial void LogSenderRejected(string sender);

    [LoggerMessage(EventId = 1124, Level = LogLevel.Warning, Message = "Microsoft Graph lookup failed for {HeaderType} address {Sender}")]
    private partial void LogSenderTenantLookupFailed(Exception exception, string headerType, string sender);
}

public class ResolvedSender
{
    public string? Name { get; set; }
    public string? Address { get; set; }
    public Microsoft.Graph.Models.User? User { get; set; }
    public SmtpResponse? SmtpResponse { get; set; }
}