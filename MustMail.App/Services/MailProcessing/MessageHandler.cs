using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using MimeKit;
using MimeKit.Utils;
using Polly;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;
using System.Buffers;
using System.Text.Json;

namespace MustMail.App.Services.MailProcessing;

public partial class MessageHandler(ILogger<MessageHandler> logger, GraphServiceClient graphClient, IOptionsMonitor<Configuration> config, RecipientResolver recipientsResolver, ErrorNotificationHandler errorNotificationHandler, SenderResolver senderResolver, SmtpAccountAuthorization smtpAccountAuthorization, AttachmentHandler attachmentHandler, MessageStorage messageStorage, ResiliencePipeline graphSendPipeline) : MessageStore
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

        // If there is no message id create one
        if (string.IsNullOrWhiteSpace(message.MessageId)) message.MessageId = MimeUtils.GenerateMessageId();

        // Get sender from message and SMTP transaction
        ResolvedSender sender = await senderResolver.ResolveSender(transaction, message);

        // If an SMTP response is provided return it
        if (sender.SmtpResponse != null)
        {
            await errorNotificationHandler.Notify(sender.FailureReason!, message, sender, cancellationToken);
            return sender.SmtpResponse;
        }

        // These should not be null
        if (sender.Name == null || sender.Address == null || sender.User == null)
        {
            await errorNotificationHandler.Notify("Sender resolution returned incomplete data", message, sender, cancellationToken);
            return SmtpResponse.SyntaxError;
        }

        bool senderAllowed = await smtpAccountAuthorization.CheckSenderIsAllowed(context.Authentication.User, sender.Address);
        if (!senderAllowed)
        {
            LogSenderNotAllowed(context.Authentication.User, sender.Address);
            await errorNotificationHandler.Notify($"Sender {sender.Address} is not permitted for SMTP account '{context.Authentication.User}'", message, sender, cancellationToken);
            return SmtpResponse.MailboxNameNotAllowed;
        }

        // Get recipients from message and SMTP transaction
        ResolvedRecipients recipients = recipientsResolver.ResolveRecipients(transaction, message);

        // If all recipients were filtered out we won't send the email
        if (recipients.All.Count == 0)
        {
            LogNoRecipients();
            string rejectedList = recipients.Rejected.Count > 0
                ? $"Rejected by global allowed recipients list: {string.Join(", ", recipients.Rejected.Select(r => r.EmailAddress?.Address))}"
                : "No recipients were provided";
            await errorNotificationHandler.Notify(rejectedList, message, sender, cancellationToken);
            return SmtpResponse.NoValidRecipientsGiven;
        }

        List<Recipient> accountRejected = await smtpAccountAuthorization.CheckRecipientIsAllowed(context.Authentication.User, recipients.All);
        if (accountRejected.Count > 0)
        {
            LogRecipientsNotAllowed(context.Authentication.User);
            string rejectedList = string.Join(", ", accountRejected.Select(r => r.EmailAddress?.Address));
            await errorNotificationHandler.Notify($"Recipients rejected by SMTP account '{context.Authentication.User}' allowed list: {rejectedList}", message, sender, cancellationToken);
            return SmtpResponse.NoValidRecipientsGiven;
        }

       

        List<Attachment> attachments = [];
        
        // If message contains attachments then extract them from the message
        if (message.Attachments.Any())
            attachments = await attachmentHandler.HandelAttachments(message);


        // If store emails is enabled for each recipient that has an account store a copy of the email on disk
        if (config.CurrentValue.Mail.StoreMail)
        {
            await messageStorage.StoreMessage(message, recipients, sender);
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
                Content = message.HtmlBody + (config.CurrentValue.Mail.FooterBranding ? $"<br><br>---<p style=\"font-size:12px;color:#666;\">Sent via self‑hosted <a href=\"https://mustmail.net\">MustMail</a></p>" : "")
            }
            // Else use the text body instead
            : new ItemBody
            {
                ContentType = BodyType.Text,

                Content = message.TextBody + (config.CurrentValue.Mail.FooterBranding ? $"\n\n---\nSent via self-hosted MustMail(https://mustmail.net)" : "")
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

        ResilienceContext resilienceContext = ResilienceContextPool.Shared.Get(message.MessageId, cancellationToken);
        try
        {
            await graphSendPipeline.ExecuteAsync(async ctx =>
                await graphClient.Users[sender.User.UserPrincipalName].SendMail.PostAsync(requestBody, cancellationToken: ctx.CancellationToken),
                resilienceContext);
        }
        catch (Exception ex)
        {
            LogGraphSendFailed(ex, sender.Address);
            string allRecipients = string.Join(", ", recipients.All.Select(r => r.EmailAddress?.Address));
            await errorNotificationHandler.Notify($"Microsoft Graph failed to send from {sender.Address} to [{allRecipients}]: {ex.Message}", message, sender, cancellationToken);
            return SmtpResponse.SyntaxError;
        }
        finally
        {
            ResilienceContextPool.Shared.Return(resilienceContext);
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

    [LoggerMessage(EventId = 1105, Level = LogLevel.Warning, Message = "Message rejected: The SMTP account: {Account} is not allowed to send mail to one or more of the recipients")]
    private partial void LogRecipientsNotAllowed(string account);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Warning, Message = "Message rejected: The SMTP account: {Account} is not allowed to send mail from {Sender}")]
    private partial void LogSenderNotAllowed(string account, string sender);

    [LoggerMessage(EventId = 1107, Level = LogLevel.Debug, Message = "Sending email via Microsoft Graph: \n{Message}")]
    private partial void LogGraphSendAttempt(string message);

    [LoggerMessage(EventId = 1108, Level = LogLevel.Error, Message = "Failed to send email via Microsoft Graph for sender {Sender}")]
    private partial void LogGraphSendFailed(Exception exception, string sender);

    [LoggerMessage(EventId = 1109, Level = LogLevel.Information, Message = "Email forwarded successfully. Subject: {Subject}, Sender: {Sender} as the User(UPN): {User},  Recipients; To: {To}, Cc: {Cc}, Bcc: {Bcc}")]
    private partial void LogEmailForwarded(string? subject, string sender, string user, IEnumerable<string> to, IEnumerable<string> cc, IEnumerable<string> bcc);


}