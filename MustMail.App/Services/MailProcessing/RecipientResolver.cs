using Microsoft.Graph.Models;
using MimeKit;

namespace MustMail.App.Services.MailProcessing;

public partial class RecipientResolver(ILogger<RecipientResolver> logger, IConfiguration config)
{
    private readonly MustMailConfiguration _mustMailConfig = config.Get<Configuration>()!.MustMail;

    public ResolvedRecipients? ResolveRecipients(SmtpServer.IMessageTransaction transaction, MimeMessage message)
    {
        
        // Extract envelope recipients from the SMTP transaction
        List<Recipient> envelopeRecipients = transaction.To.Select(mailbox => new Recipient
        {
            EmailAddress = new EmailAddress
            {
                Address = $"{mailbox.User.Trim()}@{mailbox.Host.Trim()}"
            }
        }).ToList();
        
        // Create a list to store all recipients (To, CC, BCC)
        List<Recipient>
            allRecipients = [];

        // Create list of To recipients
        List<Recipient> toRecipients =
        [
            .. message.To
                .OfType<MailboxAddress>()
                .Where(address => !string.IsNullOrWhiteSpace(address.Address))// filter out null/empty
                .Select(address => new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = address.Address,// plain email only
                        Name = address.Name// optional, can be null or empty
                    }
                })
        ];

        // Create list of Cc recipients
        List<Recipient> ccRecipients =
        [
            .. message.Cc
                .OfType<MailboxAddress>()
                .Where(address => !string.IsNullOrWhiteSpace(address.Address))// filter out null/empty
                .Select(address => new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = address.Address,// plain email only
                        Name = address.Name// optional, can be null or empty
                    }
                })
        ];
        
        // If there are to recipients then process them 
        if (toRecipients.Count != 0)
        {
            // For each to recipient remove them from envelopeRecipients
            foreach (Recipient to in toRecipients)
            {
                Recipient? match = envelopeRecipients.FirstOrDefault(recipient =>
                                                                         string.Equals(recipient.EmailAddress?.Address,
                                                                                       to.EmailAddress?.Address,
                                                                                       StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    envelopeRecipients.Remove(match);
                }
            }

            allRecipients.AddRange(from Recipient recipient in toRecipients
                                   select recipient);
            // Debug log the To recipients
            if (logger.IsEnabled(LogLevel.Debug))
            {
                IEnumerable<string> to = toRecipients.Select(r => r.EmailAddress!.Address!);
                LogToRecipientsResolved(to);
            }
        }
        
        // If there are cc recipients then process them 
        if (ccRecipients.Count != 0)
        {
            // For each cc recipient remove them from envelopeRecipients
            foreach (Recipient cc in ccRecipients)
            {
                Recipient? match = envelopeRecipients.FirstOrDefault(recipient =>
                                                                         string.Equals(recipient.EmailAddress?.Address,
                                                                                       cc.EmailAddress?.Address,
                                                                                       StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    envelopeRecipients.Remove(match);
                }
            }

            allRecipients.AddRange(from Recipient recipient in ccRecipients
                                   select recipient);
            // Debug log the Cc recipients
            if (logger.IsEnabled(LogLevel.Debug))
            {
                IEnumerable<string> cc = ccRecipients.Select(r => r.EmailAddress!.Address!);
                LogCcRecipientsResolved(cc);
            }
        }

        // Bcc recipients are not included in MimeMessage as such if the address still exists in envelopeRecipients then send bcc
        List<Recipient> bccRecipients = [];
        bccRecipients.AddRange(envelopeRecipients);

        if (bccRecipients.Count != 0)
        {
            allRecipients.AddRange(from Recipient recipient in bccRecipients
                                   select recipient);
            // Debug log the Bcc recipients
            if (logger.IsEnabled(LogLevel.Debug))
            {
                IEnumerable<string> bcc = bccRecipients.Select(r => r.EmailAddress!.Address!);
                LogBccRecipientsResolved(bcc);
            }
        }
        
        // Create a list to store the rejected recipients
        List<Recipient> rejectedRecipients = [];

        // If allowedTo is not wildcard and there are allowed recipients in the list then loop through each one
        if (!_mustMailConfig.AllowedRecipients.Contains("*") && _mustMailConfig.AllowedRecipients.Count > 0)
        {
            foreach (Recipient recipient in allRecipients)
            {
                // Extract email address only
                string recipientAddress = recipient.EmailAddress!.Address!;
                
                // Check if address is allowed
                bool isAllowed = _mustMailConfig.AllowedRecipients.Any(allowed => {

                    // Direct comparison of address eg. user@example.com compared to user@example.com
                    if (string.Equals(
                                      allowed,
                                      recipientAddress,
                                      StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // Comparison of domain eg. example.com compared to example.com
                    if (allowed.StartsWith("*@"))
                    {
                        return recipientAddress.EndsWith(
                                                         allowed[1..], // This extracts only the domain 
                                                         StringComparison.OrdinalIgnoreCase);
                    }

                    return false;
                });
                
                // If the address is not allowed add them to the rejected recipients list
                if (!isAllowed)
                {
                    rejectedRecipients.Add(recipient);
                }
            }
        }
        
        // If some recipients have been rejected loop through and remove them from the email
        if (rejectedRecipients.Count != 0)
        {
            foreach (Recipient recipient in rejectedRecipients)
            {
                allRecipients.Remove(recipient);
                toRecipients.Remove(recipient);
                ccRecipients.Remove(recipient);
                bccRecipients.Remove(recipient);

                LogRecipientRemoved(recipient.EmailAddress!.Address!);
            }
        }

        // Check we have recipients
        if (allRecipients.Count == 0)
        {
            return null;
        }

        return new ResolvedRecipients
        {
            All = allRecipients,
            To = toRecipients,
            Cc = ccRecipients,
            Bcc = bccRecipients
        };
    }

    // 1110s = RecipientResolver
    [LoggerMessage(EventId = 1110, Level = LogLevel.Debug, Message = "To recipients resolved: {Recipients}")]
    private partial void LogToRecipientsResolved(IEnumerable<string> recipients);

    [LoggerMessage(EventId = 1111, Level = LogLevel.Debug, Message = "Cc recipients resolved: {Recipients}")]
    private partial void LogCcRecipientsResolved(IEnumerable<string> recipients);

    [LoggerMessage(EventId = 1112, Level = LogLevel.Debug, Message = "Bcc recipients resolved: {Recipients}")]
    private partial void LogBccRecipientsResolved(IEnumerable<string> recipients);

    [LoggerMessage(EventId = 1113, Level = LogLevel.Warning, Message = "Recipient {Recipient} removed because it is not in the allowed recipient list")]
    private partial void LogRecipientRemoved(string recipient);
}

public class ResolvedRecipients
{
    public List<Recipient> To { get; init; } = [];
    public List<Recipient> Cc { get; init; } = [];
    public List<Recipient> Bcc { get; init; } = [];
    public List<Recipient> All { get; init; } = [];
}