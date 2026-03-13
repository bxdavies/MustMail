using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using MimeKit;
using MimeKit.Utils;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using System.Buffers;
using System.Text.Json;

namespace MustMail.MailServer;

public partial class MessageHandler(ILogger<MessageHandler> logger, GraphServiceClient graphClient, IDbContextFactory<DatabaseContext> dbFactory, MustMailConfiguration mustMailConfiguration, UpdateService updates, GraphUserHelper graphUserHelper) : MessageStore
{
    public override async Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {

        // Debug log for when this function is called
        LogMessageReceived();

        // Create memory stream
        await using MemoryStream stream = new();
        
        foreach (ReadOnlyMemory<byte> memory in buffer)
        {
            await stream.WriteAsync(memory, cancellationToken);
        }
        
        // Debug log for the raw message
        LogMessageSize(buffer.Length);

        // Set stream position back to 0
        stream.Position = 0;

        // Load the memory stream as a Mime Message
        MimeMessage message = await MimeMessage.LoadAsync(stream, cancellationToken);

        // Debug log for the Mime Message
        if (logger.IsEnabled(LogLevel.Debug))
        {
#pragma warning disable CA1873 // Avoid potentially expensive logging
            LogMimeParsed(message.Subject, message.Attachments.Count());
#pragma warning restore CA1873 // Avoid potentially expensive logging
        }

        List<Recipient> envelopeRecipients = transaction.To.Select(mailbox => new Recipient
        {
            EmailAddress = new EmailAddress
            {
                Address = $"{mailbox.User.Trim()}@{mailbox.Host.Trim()}"
            }
        }).ToList();
        
        List<Recipient>
            allRecipients = [];

        // Create list of To recipients
        List<Recipient> toRecipients = [.. message.To
            .OfType<MailboxAddress>()
            .Where(address => !string.IsNullOrWhiteSpace(address.Address))   // filter out null/empty
            .Select(address => new Recipient
            {
                EmailAddress = new EmailAddress
                {
                    Address = address.Address,  // plain email only
                    Name = address.Name        // optional, can be null or empty
                }
            })];

        // Create list of Cc recipients
        List<Recipient> ccRecipients = [.. message.Cc
            .OfType<MailboxAddress>()
            .Where(address => !string.IsNullOrWhiteSpace(address.Address))   // filter out null/empty
            .Select(address => new Recipient
            {
                EmailAddress = new EmailAddress
                {
                    Address = address.Address,  // plain email only
                    Name = address.Name        // optional, can be null or empty
                }
            })];
        
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

        // Check we have recipients - this should never happen but for sanity we check
        if (allRecipients.Count == 0)
        {
            LogNoRecipients();
            return SmtpResponse.NoValidRecipientsGiven;
        }

        // If allowedTo is not wildcard
        if (!mustMailConfiguration.AllowedRecipients.Contains("*") && mustMailConfiguration.AllowedRecipients.Count > 0)
        {
            foreach (Recipient recipient in allRecipients)
            {
                // If the sender does not exist in the allowedFrom list then throw an error and return
                if (!mustMailConfiguration.AllowedRecipients.Contains(recipient.EmailAddress!.Address!))
                {
                    LogRecipientRejected(recipient.EmailAddress!.Address!);
                    return SmtpResponse.SyntaxError;
                }
            }
        }

        Microsoft.Graph.Models.User? user;

        // Try MAIL FROM first (preferred)
        string? senderAddress =
            transaction.From == null ||
            string.IsNullOrWhiteSpace(transaction.From.User) ||
            string.IsNullOrWhiteSpace(transaction.From.Host)
                ? null
                : $"{transaction.From.User.Trim()}@{transaction.From.Host.Trim()}";
        string? senderName = message.Sender?.Name?.Trim();

        // Check MAIL FROM was found
        if (!string.IsNullOrWhiteSpace(senderAddress))
        {
            // Attempt to get the user from graph by UPN, Mail or any alias addresses
            try
            {
                user = await graphUserHelper.FindSenderUserAsync(
                    "MAIL FROM",
                    senderAddress,
                    cancellationToken);

                if (user == null)
                {
                    return SmtpResponse.MailboxUnavailable;
                }
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError error)
            {
                LogSenderTenantLookupFailed(error, "MAIL FROM", senderAddress);
                return SmtpResponse.SyntaxError;
            }

        }
        // MAIL FROM was not found falling back to FROM
        else
        {
            // Check if we trust the FROM address
            if (!mustMailConfiguration.TrustFrom)
            {
                LogMailFromMissingTrustFromDisabled();
                return SmtpResponse.SyntaxError;
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
                return SmtpResponse.MailboxUnavailable;
            }

            LogUsingFromFallback(senderAddress);

            // Attempt to get the user from graph by UPN, Mail or any alias addresses
            try
            {
                user = await graphUserHelper.FindSenderUserAsync(
                    "FROM",
                    senderAddress,
                    cancellationToken);

                if (user == null)
                {
                    return SmtpResponse.MailboxUnavailable;
                }
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError error)
            {
                LogSenderTenantLookupFailed(error, "FROM", senderAddress);
                return SmtpResponse.SyntaxError;
            }
        }

        // If the sender does not exist in the allowedFrom list then throw an error and return
        if (!mustMailConfiguration.AllowedSenders.Contains("*") && mustMailConfiguration.AllowedSenders.Count > 0)
        {
            if (!mustMailConfiguration.AllowedSenders.Contains(senderAddress))
            {
                LogSenderRejected(senderAddress);
                return SmtpResponse.SyntaxError;
            }
        }

        // Extract and process attachments
        List<Attachment> attachments = [];

        // If the message has attachments then add them
        if (message.Attachments.Any())
        {

            foreach (MimeEntity mimeEntity in message.Attachments)
            {
                switch (mimeEntity)
                {
                    // Regular file attachment
                    case MimePart { Content: not null } mimePart:
                    {
                        string fileName = mimePart.FileName ?? "unnamed-attachment";

                        // Replace invalid characters with hyphens
                        Array.ForEach(Path.GetInvalidFileNameChars(),
                            c => fileName = fileName.Replace(c.ToString(), "-"));

                        // Write to byte stream
                        using MemoryStream memory = new();
                        await mimePart.Content.DecodeToAsync(memory, cancellationToken);
                        byte[] attachmentBytes = memory.ToArray();

                        // Create graph attachment 
                        attachments.Add(new FileAttachment
                        {
                            OdataType = "#microsoft.graph.fileAttachment",
                            Name = fileName,
                            ContentType = mimePart.ContentType.MimeType,
                            ContentBytes = attachmentBytes
                        });

                        LogAttachment(fileName, attachmentBytes.Length, mimePart.ContentType.MimeType);
                        break;
                    }
                    // Embedded email message
                    case MessagePart { Message: not null } messagePart:
                    {
                        string embeddedName = messagePart.Message.Subject ?? "embedded-message";

                        // Replace invalid characters with hyphens
                        Array.ForEach(Path.GetInvalidFileNameChars(),
                            c => embeddedName = embeddedName.Replace(c.ToString(), "-"));

                        // Write to byte stream
                        using MemoryStream memory = new();
                        await messagePart.Message.WriteToAsync(memory, cancellationToken);
                        byte[] messageBytes = memory.ToArray();

                        // Create graph attachment 
                        attachments.Add(new FileAttachment
                        {
                            OdataType = "#microsoft.graph.fileAttachment",
                            Name = embeddedName + ".eml",
                            ContentType = "message/rfc822",
                            ContentBytes = messageBytes
                        });

                        LogEmbeddedMessage(embeddedName, messageBytes.Length);
                        break;
                    }
                }
            }
        }


        // If store emails is enabled for each recipient that has an account store a copy of the email on disk
        if (mustMailConfiguration.StoreEmails)
        {
            foreach (Recipient recipient in allRecipients)
            {
                await using DatabaseContext dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);

                // If email address is null skip
                if (recipient.EmailAddress?.Address == null)
                    continue;

                // Get user from database
                Models.User? appUser = await dbContext.User.SingleOrDefaultAsync(u => u.Email == recipient.EmailAddress.Address, cancellationToken: cancellationToken);

                // Handel users that don't have an account
                if (appUser == null)
                    continue;

                // If there is no message id create one
                if (string.IsNullOrWhiteSpace(message.MessageId)) message.MessageId = MimeUtils.GenerateMessageId();

                // Add message to database
                appUser.Messages.Add(new Models.Message
                {
                    Id = message.MessageId,
                    SenderName = senderName ?? senderAddress,
                    SenderEmail = senderAddress,
                    Timestamp = message.Date.DateTime.ToUniversalTime(),
                    Subject = message.Subject ?? "(No subject)",
                    AttachmentCount = message.Attachments.Count()
                });

                _ = await dbContext.SaveChangesAsync(cancellationToken);

                // Create a path maildrop/userId/messageId.eml
                string emailPath = Path.Combine(
                   AppContext.BaseDirectory,
                   "Data",
                   "maildrop",
                   appUser.Id,
                   $"{message.MessageId}.eml");

                // Sanitize file path
                emailPath = Helpers.SanitizeFilePath(emailPath);

                // Ensure all directories in this path exist
                _ = Directory.CreateDirectory(Path.GetDirectoryName(emailPath)!);

                LogEmailStored(recipient.EmailAddress!.Address!, emailPath);

                // Save message to file
                await using (FileStream fileStream = File.Create(emailPath))
                {
                    await message.WriteToAsync(fileStream, cancellationToken);
                }

                // If the message has attachments then add them
                if (message.Attachments.Any())
                {

                    foreach (MimeEntity mimeEntity in message.Attachments)
                    {
                        switch (mimeEntity)
                        {
                            // Regular file attachment
                            case MimePart { Content: not null } mimePart:
                            {
                                string fileName = mimePart.FileName ?? "unnamed-attachment";

                                // Create path maildrop/userId/messageId/filename
                                string attachmentPath = Path.Combine(
                                    AppContext.BaseDirectory,
                                    "Data",
                                    "maildrop",
                                    appUser.Id,
                                    message.MessageId,
                                    fileName);

                                // Sanitize file path
                                attachmentPath = Helpers.SanitizeFilePath(attachmentPath);

                                // Ensure directory exists
                                _ = Directory.CreateDirectory(Path.GetDirectoryName(attachmentPath)!);

                                // Write file
                                await using FileStream fileStream = File.Create(attachmentPath);
                                await mimePart.Content.DecodeToAsync(fileStream, cancellationToken);
                                break;
                            }
                            // Embedded email message
                            case MessagePart { Message: not null } messagePart:
                            {
                                string embeddedName = messagePart.Message.Subject ?? "embedded-message";

                                // Create path maildrop/userId/messageId/filename
                                string attachmentPath = Path.Combine(
                                    AppContext.BaseDirectory,
                                    "Data",
                                    "maildrop",
                                    appUser.Id,
                                    message.MessageId,
                                    $"{embeddedName}.eml");

                                // Sanitize file path
                                attachmentPath = Helpers.SanitizeFilePath(attachmentPath);

                                // Ensure directory exists
                                _ = Directory.CreateDirectory(Path.GetDirectoryName(attachmentPath)!);

                                // Write file
                                await using FileStream fileStream = File.Create(attachmentPath);
                                await messagePart.Message.WriteToAsync(fileStream, cancellationToken);
                                break;
                            }
                        }
                    }
                }

                // Trigger update service to update any clients
                await updates.NewMessageForUserAsync(appUser.Id);
                LogClientUpdate(appUser.Id);
            }
        }

        // Create message 
        SendMailPostRequestBody requestBody = new()
        {
            Message = new Microsoft.Graph.Models.Message
            {
                Subject = message.Subject,
                From = new Recipient
                {
                    EmailAddress = new EmailAddress
                    {
                        Address = senderAddress,  // plain email only
                        Name = senderName,
                    }
                },
                ToRecipients = toRecipients,
                CcRecipients = ccRecipients,
                BccRecipients = bccRecipients,
                Attachments = attachments,
            }
        };

        // If message does contain an HTML body then use it
        requestBody.Message.Body = message.HtmlBody != null
            ? new ItemBody
            {
                ContentType = BodyType.Html,
                Content = message.HtmlBody + (mustMailConfiguration.FooterBranding ? $"<br><br>---<p style=\"font-size:12px;color:#666;\">Sent via self‑hosted <a href=\"https://mustmail.net\">MustMail</a></p>" : "")
            }
            // Else use the text body instead
            : new ItemBody
            {
                ContentType = BodyType.Text,

                Content = message.TextBody + (mustMailConfiguration.FooterBranding ? $"\n\n---\nSent via self-hosted MustMail(https://mustmail.net)" : "")
            };

        // Log email details if debug log level is enabled 
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var emailInfo = new
            {
                Subject = message.Subject ?? "(no subject)",
                From = senderAddress,
                To = toRecipients.Select(u => u.EmailAddress?.Address).OfType<string>(),
                Cc = ccRecipients.Select(u => u.EmailAddress?.Address).OfType<string>(),
                BccRecipients = bccRecipients.Select(u => u.EmailAddress?.Address).OfType<string>(),
                AttachmentCount = attachments.Count,
                requestBody.Message.Body
            };
            string emailInfoJson = JsonSerializer.Serialize(emailInfo);
            LogGraphSendAttempt(emailInfoJson);
        }

        try
        {
            // Send email
            await graphClient.Users[user.UserPrincipalName].SendMail.PostAsync(requestBody, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            LogGraphSendFailed(ex, senderAddress);
            return SmtpResponse.SyntaxError;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            // Build lists of to, cc and bcc
            IEnumerable<string> to = toRecipients.Select(u => u.EmailAddress?.Address).OfType<string>();
            IEnumerable<string> cc = ccRecipients.Select(u => u.EmailAddress?.Address).OfType<string>();
            IEnumerable<string> bcc = bccRecipients.Select(u => u.EmailAddress?.Address).OfType<string>();

            // Log success message
            LogEmailForwarded(
                message.Subject,
                senderAddress,
                user.UserPrincipalName!,
                to, cc, bcc);
        }

        // Return email received successfully
        return SmtpResponse.Ok;

    }

    // 1100s = MessageHandler

    [LoggerMessage(EventId = 1101, Level = LogLevel.Debug, Message = "Incoming SMTP message received")]
    private partial void LogMessageReceived();

    [LoggerMessage(EventId = 1102, Level = LogLevel.Debug, Message = "SMTP message size: {MessageSize} bytes")]
    private partial void LogMessageSize(long messageSize);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Debug, Message = "MIME message parsed. Subject: {Subject}, AttachmentCount: {AttachmentCount}")]
    private partial void LogMimeParsed(string? subject, int attachmentCount);
    
    [LoggerMessage(EventId = 11051, Level = LogLevel.Debug, Message = "To recipients resolved: {Recipients}")]
    private partial void LogToRecipientsResolved(IEnumerable<string> recipients);

    [LoggerMessage(EventId = 11052, Level = LogLevel.Debug, Message = "Cc recipients resolved: {Recipients}")]
    private partial void LogCcRecipientsResolved(IEnumerable<string> recipients);

    [LoggerMessage(EventId = 11053, Level = LogLevel.Debug, Message = "Bcc recipients resolved: {Recipients}")]
    private partial void LogBccRecipientsResolved(IEnumerable<string> recipients);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Warning, Message = "Message rejected: no valid recipients were found")]
    private partial void LogNoRecipients();

    [LoggerMessage(EventId = 1107, Level = LogLevel.Warning, Message = "Recipient {Recipient} rejected because it is not in the allowed recipient list")]
    private partial void LogRecipientRejected(string recipient);

    [LoggerMessage(EventId = 1109, Level = LogLevel.Information, Message = "Falling back to From header sender {Sender}")]
    private partial void LogUsingFromFallback(string sender);

    [LoggerMessage(EventId = 1110, Level = LogLevel.Warning, Message = "MAIL FROM missing and TrustFrom is disabled. Message rejected")]
    private partial void LogMailFromMissingTrustFromDisabled();

    [LoggerMessage(EventId = 1111, Level = LogLevel.Warning, Message = "MAIL FROM missing and no usable From header address was found. Message rejected")]
    private partial void LogFromHeaderMissing();

    [LoggerMessage(EventId = 1112, Level = LogLevel.Warning, Message = "Sender {Sender} rejected because it is not in the allowed sender list")]
    private partial void LogSenderRejected(string sender);

    [LoggerMessage(EventId = 1116, Level = LogLevel.Warning, Message = "Microsoft Graph lookup failed for {HeaderType} address {Sender}")]
    private partial void LogSenderTenantLookupFailed(Exception exception, string headerType, string sender);

    [LoggerMessage(EventId = 1117, Level = LogLevel.Information, Message = "Email stored locally for {Recipient} at {Path}")]
    private partial void LogEmailStored(string recipient, string path);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing attachment: {FileName}, Size: {Size} bytes, Type: {ContentType}")]
    private partial void LogAttachment(string fileName, int size, string contentType);
    [LoggerMessage(Level = LogLevel.Debug, Message = "Processing embedded message: {Name}, Size: {Size} bytes")]
    private partial void LogEmbeddedMessage(string name, int size);

    [LoggerMessage(EventId = 1118, Level = LogLevel.Debug, Message = "Sending email via Microsoft Graph: \n{Message}")]
    private partial void LogGraphSendAttempt(string message);

    [LoggerMessage(EventId = 1119, Level = LogLevel.Error, Message = "Failed to send email via Microsoft Graph for sender {Sender}")]
    private partial void LogGraphSendFailed(Exception exception, string sender);

    [LoggerMessage(EventId = 1120, Level = LogLevel.Information, Message = "Email forwarded successfully. Subject: {Subject}, Sender: {Sender} as the User(UPN): {User},  Recipients; To: {To}, Cc: {Cc}, Bcc: {Bcc}")]
    private partial void LogEmailForwarded(string? subject, string sender, string user, IEnumerable<string> to, IEnumerable<string> cc, IEnumerable<string> bcc);

    [LoggerMessage(EventId = 1121, Level = LogLevel.Debug, Message = "Notified clients of mailbox update for user {UserId}")]
    private partial void LogClientUpdate(string userId);
}