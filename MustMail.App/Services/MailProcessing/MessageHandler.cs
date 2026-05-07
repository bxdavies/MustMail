using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using System.Buffers;
using System.Text.Json;

namespace MustMail.App.Services.MailProcessing;

public partial class MessageHandler(ILogger<MessageHandler> logger, GraphServiceClient graphClient, MustMailConfiguration mustMailConfig, RecipientResolver recipientsResolver, SenderResolver senderResolver, AttachmentHandler attachmentHandler, MessageStorage messageStorage) : MessageStore
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
#pragma warning disable CA1873// Avoid potentially expensive logging
            LogMimeParsed(message.Subject, message.Attachments.Count());
#pragma warning restore CA1873// Avoid potentially expensive logging
        }

        // Get recipients from message and SMTP transaction
        ResolvedRecipients? recipients = recipientsResolver.ResolveRecipients(transaction, message);

        // If recipients are null then we won't send the email
        if (recipients == null)
        {
            LogNoRecipients();
            return SmtpResponse.NoValidRecipientsGiven;
        }

        // Get sender from message and SMTP transaction
        ResolvedSender sender = await senderResolver.ResolveSender(transaction, message);

        // If an SMTP response is provided return it
        if (sender.SmtpResponse != null)
        {
            return sender.SmtpResponse;
        }

        // These should not be null
        if (sender.Name == null || sender.Address == null || sender.User == null)
        {
            return SmtpResponse.SyntaxError;
        }

        List<Attachment> attachments = [];
        
        // If message contains attachments then extract them from the message
        if (message.Attachments.Any())
            attachments = await attachmentHandler.HandelAttachments(message);


        // If store emails is enabled for each recipient that has an account store a copy of the email on disk
        if (mustMailConfig.StoreEmails)
        {
            messageStorage.StoreMessage(message, recipients, sender);
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
                        Address = sender.Address,// plain email only
                        Name = sender.Name
                    }
                },
                ToRecipients = recipients.To,
                CcRecipients = recipients.Cc,
                BccRecipients = recipients.Bcc,
                Attachments = attachments
            }
        };

        // If message does contain an HTML body then use it
        requestBody.Message.Body = message.HtmlBody != null
            ? new ItemBody
            {
                ContentType = BodyType.Html,
                Content = message.HtmlBody + (mustMailConfig.FooterBranding ? $"<br><br>---<p style=\"font-size:12px;color:#666;\">Sent via self‑hosted <a href=\"https://mustmail.net\">MustMail</a></p>" : "")
            }
            // Else use the text body instead
            : new ItemBody
            {
                ContentType = BodyType.Text,

                Content = message.TextBody + (mustMailConfig.FooterBranding ? $"\n\n---\nSent via self-hosted MustMail(https://mustmail.net)" : "")
            };

        // Log email details if debug log level is enabled 
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var emailInfo = new
            {
                Subject = message.Subject ?? "(no subject)",
                From = sender.Address,
                To = recipients.To.Select(u => u.EmailAddress?.Address).OfType<string>(),
                Cc = recipients.Cc.Select(u => u.EmailAddress?.Address).OfType<string>(),
                BccRecipients = recipients.Bcc.Select(u => u.EmailAddress?.Address).OfType<string>(),
                AttachmentCount = attachments.Count,
                requestBody.Message.Body
            };
            string emailInfoJson = JsonSerializer.Serialize(emailInfo);
            LogGraphSendAttempt(emailInfoJson);
        }

        try
        {
            // Send email
            await graphClient.Users[sender.User.UserPrincipalName].SendMail.PostAsync(requestBody, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            LogGraphSendFailed(ex, sender.Address);
            return SmtpResponse.SyntaxError;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            // Build lists of to, cc and bcc
            IEnumerable<string> to = recipients.To.Select(u => u.EmailAddress?.Address).OfType<string>();
            IEnumerable<string> cc = recipients.Cc.Select(u => u.EmailAddress?.Address).OfType<string>();
            IEnumerable<string> bcc = recipients.Bcc.Select(u => u.EmailAddress?.Address).OfType<string>();

            // Log success message
            LogEmailForwarded(
                              message.Subject,
                              sender.Address,
                              sender.User.UserPrincipalName!,
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

    [LoggerMessage(EventId = 1104, Level = LogLevel.Warning, Message = "Message rejected: no valid recipients were found")]
    private partial void LogNoRecipients();

    [LoggerMessage(EventId = 1105, Level = LogLevel.Debug, Message = "Sending email via Microsoft Graph: \n{Message}")]
    private partial void LogGraphSendAttempt(string message);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Error, Message = "Failed to send email via Microsoft Graph for sender {Sender}")]
    private partial void LogGraphSendFailed(Exception exception, string sender);

    [LoggerMessage(EventId = 1107, Level = LogLevel.Information, Message = "Email forwarded successfully. Subject: {Subject}, Sender: {Sender} as the User(UPN): {User},  Recipients; To: {To}, Cc: {Cc}, Bcc: {Bcc}")]
    private partial void LogEmailForwarded(string? subject, string sender, string user, IEnumerable<string> to, IEnumerable<string> cc, IEnumerable<string> bcc);


}