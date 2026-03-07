using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using MimeKit;
using MimeKit.Utils;
using MustMail.Db;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using System.Buffers;
using System.Text.Json;

namespace MustMail.MailServer;

public partial class MessageHandler(ILogger<MessageHandler> logger, GraphServiceClient graphClient, IDbContextFactory<DatabaseContext> dbFactory, MustMailConfiguration mustMailConfiguration, UpdateService updates) : MessageStore
{
    public override async Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {

        // Debug log for when this function is called
        LogMessageReceived();

        // Create memory stream
        await using MemoryStream stream = new();

        // Get position 0 
        SequencePosition position = buffer.GetPosition(0);

        // Read buffer and write to memory stream
        while (buffer.TryGet(ref position, out ReadOnlyMemory<byte> memory))
        {
            await stream.WriteAsync(memory, cancellationToken);
        }

        // Get position 0 
        position = buffer.GetPosition(0);

        // Debug log for the raw message
        LogMessageSize(buffer.Length);

        // Set stream position back to 0
        stream.Position = 0;

        // Load the memory stream as a Mime Message
        MimeMessage? message = await MimeMessage.LoadAsync(stream, cancellationToken);

        // Debug log for the Mime Message
        if (logger.IsEnabled(LogLevel.Debug))
        {
#pragma warning disable CA1873 // Avoid potentially expensive logging
            LogMimeParsed(message.Subject, message.Attachments.Count());
#pragma warning restore CA1873 // Avoid potentially expensive logging
        }


        // If message is null then return an error
        if (message == null)
        {
            LogMimeMessageNull();
            return SmtpResponse.SyntaxError;
        }

        List<Recipient>
            allRecipients = [];

        // Create list of To recipients
        List<Recipient> toRecipients = [.. message.To
            .OfType<MimeKit.MailboxAddress>()
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
            .OfType<MimeKit.MailboxAddress>()
            .Where(address => !string.IsNullOrWhiteSpace(address.Address))   // filter out null/empty
            .Select(address => new Recipient
            {
                EmailAddress = new EmailAddress
                {
                    Address = address.Address,  // plain email only
                    Name = address.Name        // optional, can be null or empty
                }
            })];

        // Create list of Bcc recipients
        List<Recipient> bccRecipients = [.. message.Bcc
            .OfType<MimeKit.MailboxAddress>()
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
            allRecipients.AddRange(from Recipient recipient in toRecipients
                                select recipient);
            // Debug log the to recipients
            if (logger.IsEnabled(LogLevel.Debug))
            {
#pragma warning disable CA1873 // Avoid potentially expensive logging
                LogToRecipientsResolved(toRecipients.Select(r => r.EmailAddress!.Address!));
#pragma warning restore CA1873 // Avoid potentially expensive logging
            }
        }

        if (ccRecipients.Count != 0)
        {
            allRecipients.AddRange(from Recipient recipient in ccRecipients
                                select recipient);
            // Debug log the cc recipients
            if (logger.IsEnabled(LogLevel.Debug))
            {
#pragma warning disable CA1873 // Avoid potentially expensive logging
                LogCcRecipientsResolved(ccRecipients.Select(r => r.EmailAddress!.Address!));
#pragma warning restore CA1873 // Avoid potentially expensive logging
            }
        }

        if (bccRecipients.Count != 0)
        {
            allRecipients.AddRange(from Recipient recipient in bccRecipients
                                select recipient);
            // Debug log the cc recipients
            if (logger.IsEnabled(LogLevel.Debug))
            {
#pragma warning disable CA1873 // Avoid potentially expensive logging
                LogBccRecipientsResolved(bccRecipients.Select(r => r.EmailAddress!.Address!));
#pragma warning restore CA1873 // Avoid potentially expensive logging
            }
        }


        // Check we have recipients - this should never happen but for sanity we check
        if (allRecipients == null || allRecipients.Count == 0)
        {
            LogNoRecipients();
            return SmtpResponse.NoValidRecipientsGiven;
        }

        // If allowedTo is not wildcarded
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

        // Try MAIL FROM first (preferred)
        string? sender = transaction.From == null
    ? null
    : $"{transaction.From.User.Trim()}@{transaction.From.Host.Trim()}";
        string? senderName = message.Sender?.Name?.Trim();

        // Check MAIL FROM was found
        if (!string.IsNullOrWhiteSpace(sender))
        {
            // Carry out M365 sender checks
            try
            {
                Microsoft.Graph.Models.User? user = await graphClient.Users[sender].GetAsync(rc => rc.QueryParameters.Select = ["displayName", "mail", "mailboxSettings"], cancellationToken);

                if (user == null)
                {
                    LogSenderTenantMissing("MAIL FROM", sender);
                }
                else if (user.Mail == null && user.UserPrincipalName == null)
                {
                    LogSenderTenantNoMailbox("MAIL FROM", sender);
                }
                else if (user.MailboxSettings == null)
                {
                    LogSenderMailboxSettingsMissing("MAIL FROM", sender);
                }
                else
                {
                    // All checks have passed update the send from address
                    LogUsingSender("MAIL FROM", sender, user.DisplayName);
                }

            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError error)
            {
                LogSenderTenantLookupFailed(error, "MAIL FROM", sender);
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
            sender = message.From.OfType<MimeKit.MailboxAddress>()
                  .Select(a => a.Address)
                  .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

            senderName = message.From.OfType<MimeKit.MailboxAddress>()
                .Select(a => a.Name)
                .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

            // Check a FROM address was found
            if (string.IsNullOrWhiteSpace(sender))
            {
                LogFromHeaderMissing();
                return SmtpResponse.SyntaxError;
            }

            LogUsingFromFallback(sender);
            // Carry out M365 sender checks
            try
            {
                Microsoft.Graph.Models.User? user = await graphClient.Users[sender].GetAsync(rc => rc.QueryParameters.Select = ["displayName", "mail", "mailboxSettings"], cancellationToken);

                if (user == null)
                {
                    LogSenderTenantMissing("FROM", sender);
                }
                else if (user.Mail == null && user.UserPrincipalName == null)
                {
                    LogSenderTenantNoMailbox("FROM", sender);
                }
                else if (user.MailboxSettings == null)
                {
                    LogSenderMailboxSettingsMissing("FROM", sender);
                }
                else
                {
                    // All checks have passed update the send from address
                    LogUsingSender("FROM", sender, user.DisplayName);
                }

            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError error)
            {
                LogSenderTenantLookupFailed(error, "FROM", sender);
                return SmtpResponse.SyntaxError;
            }
        }

        // If the sender does not exist in the allowedFrom list then throw an error and return
        if (!mustMailConfiguration.AllowedSenders.Contains("*") && mustMailConfiguration.AllowedSenders.Count > 0)
        {
            if (!mustMailConfiguration.AllowedSenders.Contains(sender))
            {
                LogSenderRejected(sender!);
                return SmtpResponse.SyntaxError;
            }
        }

        // Extract and process attachments
        List<Microsoft.Graph.Models.Attachment> attachments = [];

        // If the message has attachments then add them
        if (message.Attachments.Any())
        {

            foreach (MimeEntity mimeEntity in message.Attachments)
            {
                // Regular file attachment
                if (mimeEntity is MimePart mimePart && mimePart.Content != null)
                {
                    using MemoryStream memory = new();
                    await mimePart.Content.DecodeToAsync(memory, cancellationToken);
                    byte[] attachmentBytes = memory.ToArray();

                    string fileName = mimePart.FileName ?? "unnamed-attachment";

                    // Replace invalid characters with hyphens
                    Array.ForEach(Path.GetInvalidFileNameChars(),
                        c => fileName = fileName.Replace(c.ToString(), "-"));

                    attachments.Add(new FileAttachment
                    {
                        OdataType = "#microsoft.graph.fileAttachment",
                        Name = fileName,
                        ContentType = mimePart.ContentType.MimeType,
                        ContentBytes = attachmentBytes
                    });

                    LogAttachment(fileName, attachmentBytes.Length, mimePart.ContentType.MimeType);
                }
                // Embedded email message
                else if (mimeEntity is MessagePart messagePart && messagePart.Message != null)
                {
                   
                    string embeddedName = messagePart.Message.Subject ?? "embedded-message";

                    // Replace invalid characters with hyphens
                    Array.ForEach(Path.GetInvalidFileNameChars(),
                        c => embeddedName = embeddedName.Replace(c.ToString(), "-"));

                    using MemoryStream memory = new();
                    await messagePart.Message.WriteToAsync(memory, cancellationToken);
                    byte[] messageBytes = memory.ToArray();

                    attachments.Add(new FileAttachment
                    {
                        OdataType = "#microsoft.graph.fileAttachment",
                        Name = embeddedName + ".eml",
                        ContentType = "message/rfc822",
                        ContentBytes = messageBytes
                    });

                    LogEmbeddedMessage(embeddedName, messageBytes.Length);
                }
            }
        }


        // If store emails is enabled for each recipient that has an account store a copy of the email on disk
        if (mustMailConfiguration.StoreEmails)
        {
            foreach (Recipient recipient in allRecipients)
            {
                await using DatabaseContext dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);

                Console.WriteLine(recipient.EmailAddress.Address);
                // If email address is null skip
                if (recipient.EmailAddress.Address == null)
                    continue;

                // Get user from database
                Models.User? user = await dbContext.User.SingleOrDefaultAsync(u => u.Email == recipient.EmailAddress.Address, cancellationToken: cancellationToken);

                // Handel users that don't have an account
                if (user == null)
                    continue;

                // If there is no message id create one
                if (string.IsNullOrWhiteSpace(message.MessageId))
                    message.MessageId = MimeUtils.GenerateMessageId();

                // Add message to database
                user.Messages.Add(new Models.Message
                {
                    Id = message.MessageId,
                    SenderName = senderName ?? sender,
                    SenderEmail = sender,
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
                   user.Id,
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
                        // Regular file attachment
                        if (mimeEntity is MimePart mimePart && mimePart.Content != null)
                        {

                            string fileName = mimePart.FileName ?? "unnamed-attachment";

                            // Create path maildrop/userId/messageId/filename
                            string attachmentPath = Path.Combine(
                                AppContext.BaseDirectory,
                                "Data",
                                "maildrop",
                                user.Id,
                                message.MessageId,
                                fileName);

                            // Sanitize file path
                            attachmentPath = Helpers.SanitizeFilePath(attachmentPath);

                            // Ensure directory exists
                            _ = Directory.CreateDirectory(Path.GetDirectoryName(attachmentPath)!);

                            await using FileStream fileStream = File.Create(attachmentPath);

                            await mimePart.Content.DecodeToAsync(fileStream, cancellationToken);
                        }
                        // Embedded email message
                        else if (mimeEntity is MessagePart messagePart && messagePart.Message != null)
                        {

                            string embeddedName = messagePart.Message.Subject ?? "embedded-message";

                             // Create path maildrop/userId/messageId/filename
                            string attachmentPath = Path.Combine(
                                AppContext.BaseDirectory,
                                "Data",
                                "maildrop",
                                user.Id,
                                message.MessageId,
                                $"{embeddedName}.eml");

                            // Sanitize file path
                            attachmentPath = Helpers.SanitizeFilePath(attachmentPath);

                            // Ensure directory exists
                            _ = Directory.CreateDirectory(Path.GetDirectoryName(attachmentPath)!);

                            await using FileStream fileStream = File.Create(attachmentPath);

                            await messagePart.Message.WriteToAsync(fileStream, cancellationToken);
                        }
                    }
                }

                // Trigger update service to update any clients
                await updates.NewMessageForUserAsync(user.Id);
                LogClientUpdate(user.Id);
            }
        }

        // Create message 
        SendMailPostRequestBody requestBody = new()
        {
            Message = new Microsoft.Graph.Models.Message
            {
                Subject = message.Subject,
                ToRecipients = toRecipients,
                CcRecipients = ccRecipients,
                BccRecipients = bccRecipients,
                Attachments = attachments,
            }
        };

        // If message does contain a HTML body then use it
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
                From = sender,
                To = toRecipients.Select(r => r.EmailAddress.Address).ToList(),
                Cc = ccRecipients.Select(r => r.EmailAddress.Address).ToList(),
                BccRecipients = bccRecipients.Select(r => r.EmailAddress.Address).ToList(),
                AttachmentCount = attachments.Count,
                requestBody.Message.Body
            };
            string emailInfoJson = JsonSerializer.Serialize(emailInfo);
            LogGraphSendAttempt(emailInfoJson);
        }
            
        try
        {
            // Send email
            await graphClient.Users[sender].SendMail.PostAsync(requestBody, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            LogGraphSendFailed(ex, sender!);
            return SmtpResponse.SyntaxError;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {

            // Log success message
#pragma warning disable CA1873 // Avoid potentially expensive logging
            LogEmailForwarded(
                message.Subject,
                sender!,
                allRecipients.Select(r => r.EmailAddress!.Address!));
#pragma warning restore CA1873 // Avoid potentially expensive logging
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

    [LoggerMessage(EventId = 1104, Level = LogLevel.Warning, Message = "Unable to read message as MIME message")]
    private partial void LogMimeMessageNull();

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

    [LoggerMessage(EventId = 1108, Level = LogLevel.Information, Message = "Using {HeaderType} sender {Sender} ({DisplayName}) for outgoing email")]
    private partial void LogUsingSender(string headerType, string sender, string? displayName);

    [LoggerMessage(EventId = 1109, Level = LogLevel.Information, Message = "Falling back to From header sender {Sender}")]
    private partial void LogUsingFromFallback(string sender);

    [LoggerMessage(EventId = 1110, Level = LogLevel.Warning, Message = "MAIL FROM missing and TrustFrom is disabled. Message rejected")]
    private partial void LogMailFromMissingTrustFromDisabled();

    [LoggerMessage(EventId = 1111, Level = LogLevel.Warning, Message = "MAIL FROM missing and no usable From header address was found. Message rejected")]
    private partial void LogFromHeaderMissing();

    [LoggerMessage(EventId = 1112, Level = LogLevel.Warning, Message = "Sender {Sender} rejected because it is not in the allowed sender list")]
    private partial void LogSenderRejected(string sender);

    [LoggerMessage(EventId = 1113, Level = LogLevel.Warning, Message = "{HeaderType} address {Sender} does not exist in the Microsoft 365 tenant")]
    private partial void LogSenderTenantMissing(string headerType, string sender);

    [LoggerMessage(EventId = 1114, Level = LogLevel.Warning, Message = "{HeaderType} address {Sender} has no mailbox configured in the tenant")]
    private partial void LogSenderTenantNoMailbox(string headerType, string sender);

    [LoggerMessage(EventId = 1115, Level = LogLevel.Warning, Message = "Mailbox settings missing for {HeaderType} address {Sender}")]
    private partial void LogSenderMailboxSettingsMissing(string headerType, string sender);

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

    [LoggerMessage(EventId = 1120, Level = LogLevel.Information, Message = "Email forwarded successfully. Subject: {Subject}, Sender: {Sender}, Recipients (To, Cc, Bcc): {Recipients}")]
    private partial void LogEmailForwarded(string? subject, string sender, IEnumerable<string> recipients);

    [LoggerMessage(EventId = 1121, Level = LogLevel.Debug, Message = "Notified clients of mailbox update for user {UserId}")]
    private partial void LogClientUpdate(string userId);
}