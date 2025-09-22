using System.Buffers;
using System.Text;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using MimeKit;
using Serilog;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;

namespace MustMail;

public class MessageHandler(GraphServiceClient graphClient, ILogger logger, string sendFrom) : MessageStore
{
    public override async Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {

        // Debug log for when this function is called
        Log.Debug("An email has been recived!");

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

        // Dbeug log for the raw message
        logger.Debug($"Raw message:\n{Encoding.UTF8.GetString(buffer.ToArray())}");

        // Set stream position back to 0
        stream.Position = 0;

        // Load the memory stream as a Mime Message
        MimeMessage? message = await MimeMessage.LoadAsync(stream, cancellationToken);

        // Debug log for the Mime Message
        logger.Debug($"Mime Message:\n {message.ToString()}");

        // If message is null then return an error
        if (message == null)
        {
            Log.Warning("Unable to read message as Mime Message!");
            return SmtpResponse.SyntaxError;
        }

        // Create list of recipients
        List<Recipient> recipients = message.To.Select(address => new Recipient { EmailAddress = new EmailAddress { Address = address.ToString() } }).ToList();

        // Create message 
        SendMailPostRequestBody requestBody = new()
        {
            Message = new Message
            {
                Subject = message.Subject,
                ToRecipients = recipients
            }

        };

        // Prefer HTML if available
        if (!string.IsNullOrEmpty(message.HtmlBody))
        {
            logger.Debug($"HTML Body:\n{message.HtmlBody}");
            requestBody.Message.Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = message.HtmlBody
            };
        }
        // If neither HtmlBody nor TextBody are populated, we need to dig deeper
        if (message.Body is TextPart textPart)
        {
            logger.Debug($"Text part:\n{textPart.Text}");
            requestBody.Message.Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = textPart.Text
            };
        }
        else if (message.Body is Multipart multipart)
        {
            logger.Debug("Multipart");
            // Try to pick the first text/* part
            foreach (var part in multipart.OfType<TextPart>())
            {
                requestBody.Message.Body = new ItemBody
                {
                    ContentType = part.IsHtml ? BodyType.Html : BodyType.Text,
                    Content = part.Text
                };
            }
        }
        else
        {
            // Nothing usable found
            logger.Warning("No usable body found in message with subject {Subject}", message.Subject);
            requestBody.Message.Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = string.Empty
            };

        }

        try
        {
            // Send email
            await graphClient.Users[sendFrom].SendMail.PostAsync(requestBody, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.Warning($"Unknown Error:\n {ex.Message}");
            return SmtpResponse.SyntaxError;
        }


        // Log success message
        logger.Information("The email with the subject `{MessageSubject}` was received and sent to `{MessageTo}` as `{From}`!", message.Subject, message.To, sendFrom);

        // Return email received successfully
        return SmtpResponse.Ok;

    }
}