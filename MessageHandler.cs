using System.Buffers;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;

namespace MustMail;

public class MessageHandler(GraphServiceClient graphClient, ILogger logger, string sendFrom) : MessageStore
{
    public override async Task<SmtpResponse> SaveAsync(ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
    {
        // Create memory stream
        await using MemoryStream stream = new();

        // Get position 0 
        SequencePosition position = buffer.GetPosition(0);
        
        // Read buffer and write to memory stream
        while (buffer.TryGet(ref position, out ReadOnlyMemory<byte> memory))
        {
            await stream.WriteAsync(memory, cancellationToken);
        }

        // Set stream position back to 0
        stream.Position = 0;

        // Load the memory stream as a Mime Message
        MimeMessage? message = await MimeMessage.LoadAsync(stream, cancellationToken);

        // If message is null then return an error
        if (message == null)
        {
            logger.LogWarning("Unable to read message as Mime Message!");
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
        
        // If message does contain a HTML body then use it
        if (message.HtmlBody != null)
        {
            requestBody.Message.Body = new ItemBody
            {
                ContentType = BodyType.Html,
                Content = message.HtmlBody
            };
        }
        // Else use the text body instead
        else
        {
            requestBody.Message.Body = new ItemBody
            {
                ContentType = BodyType.Text,
                Content = message.TextBody
            };
        }
        
        // Send email
        await graphClient.Users[sendFrom].SendMail.PostAsync(requestBody, cancellationToken: cancellationToken);
        
        // Log success message
        logger.LogInformation("The email with the subject `{MessageSubject}` was received and sent to `{MessageTo}` as `{From}`!", message.Subject, message.To, sendFrom);
       
        // Return email received successfully
        return SmtpResponse.Ok;
        
    }
}