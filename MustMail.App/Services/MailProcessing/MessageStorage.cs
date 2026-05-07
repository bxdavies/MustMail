using Microsoft.Graph.Models;
using MimeKit;
using MimeKit.Utils;
using MustMail.App.Services.Maintenance;

namespace MustMail.App.Services.MailProcessing;

public partial class MessageStorage(IDbContextFactory<DatabaseContext> dbFactory, UpdateService updates, ILogger<MessageStorage> logger)
{

    public async Task StoreMessage(MimeMessage message, ResolvedRecipients recipients, ResolvedSender sender)
    {

        foreach (Recipient recipient in recipients.All)
        {
            await using DatabaseContext dbContext = await dbFactory.CreateDbContextAsync();

            // If email address is null skip
            if (recipient.EmailAddress?.Address == null)
                continue;

            // Get user from database
            Models.User? appUser = await dbContext.User.SingleOrDefaultAsync(u => u.Email == recipient.EmailAddress.Address);

            // Handel users that don't have an account
            if (appUser == null)
                continue;

            // If there is no message id create one
            if (string.IsNullOrWhiteSpace(message.MessageId)) message.MessageId = MimeUtils.GenerateMessageId();

            // Add message to database
            appUser.Messages.Add(new Models.Message
            {
                Id = message.MessageId,
                SenderName = sender.Name,
                SenderEmail = sender.Address,
                Timestamp = message.Date.DateTime.ToUniversalTime(),
                Subject = message.Subject ?? "(No subject)",
                AttachmentCount = message.Attachments.Count()
            });

            _ = await dbContext.SaveChangesAsync();

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
                await message.WriteToAsync(fileStream);
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
                            await mimePart.Content.DecodeToAsync(fileStream);
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
                            await messagePart.Message.WriteToAsync(fileStream);
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
    // 1150s = MessageStorage
    
    [LoggerMessage(EventId = 1150, Level = LogLevel.Information, Message = "Email stored locally for {Recipient} at {Path}")]
    private partial void LogEmailStored(string recipient, string path);

    [LoggerMessage(EventId = 1151, Level = LogLevel.Debug, Message = "Notified clients of mailbox update for user {UserId}")]
    private partial void LogClientUpdate(string userId);
}