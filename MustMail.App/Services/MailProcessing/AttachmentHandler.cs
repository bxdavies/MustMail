using Microsoft.Graph.Models;
using MimeKit;

namespace MustMail.App.Services.MailProcessing;

public partial class AttachmentHandler(ILogger<AttachmentHandler> logger)
{
    public async Task<List<Attachment>> HandelAttachments(MimeMessage message)
    {
        // Create a list to store attachments
        List<Attachment> attachments = [];
        
        // Loop through each attachment in message 
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
                    await mimePart.Content.DecodeToAsync(memory);
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
                    await messagePart.Message.WriteToAsync(memory);
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

        return attachments;
    }
    // 1140s = MessageHandler 
    [LoggerMessage(EventId = 1140, Level = LogLevel.Debug, Message = "Processing attachment: {FileName}, Size: {Size} bytes, Type: {ContentType}")]
    private partial void LogAttachment(string fileName, int size, string contentType);
    [LoggerMessage(EventId = 1141, Level = LogLevel.Debug, Message = "Processing embedded message: {Name}, Size: {Size} bytes")]
    private partial void LogEmbeddedMessage(string name, int size);
}