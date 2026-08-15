using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.SendMail;
using MimeKit;
using Polly;
using Polly.Registry;

namespace MustMail.App.Services.MailProcessing
{
    public partial class ErrorNotificationHandler(IOptionsMonitor<Configuration> config, ILogger<ErrorNotificationHandler> logger, GraphUserLookupService graphUserLookupService, GraphServiceClient graphClient, ResiliencePipelineProvider<string> resiliencePipelineProvider)
    {
        public async Task Notify(string reason, MimeMessage message, ResolvedSender? sender, CancellationToken cancellationToken = default)
        {
            Microsoft.Graph.Models.User? notificationSenderUser = await graphUserLookupService.FindSenderUserAsync("MustMail__Mail__NotificationSenderAddress", config.CurrentValue.Mail.NotificationSenderAddress!);

            if (notificationSenderUser is null)
            {
                LogNotificationSenderNotFound(config.CurrentValue.Mail.NotificationSenderAddress!);
                return;
            }

            SendMailPostRequestBody requestBody = new()
            {
                Message = new Microsoft.Graph.Models.Message
                {
                    Subject = $"[MustMail] Delivery failure: {message.Subject}",
                    From = new Recipient
                    {
                        EmailAddress = new EmailAddress
                        {
                            Address = notificationSenderUser.Mail,
                            Name = notificationSenderUser.DisplayName,
                        }
                    },
                    ToRecipients = [new() { EmailAddress = new EmailAddress { Address = config.CurrentValue.Mail.NotificationRecipientAddress } }],
                    Body = new ItemBody { ContentType = BodyType.Html, Content = BuildHtmlBody(reason, message, sender) }
                }
            };

            if (config.CurrentValue.Mail.NotifyUsersOnError == true && sender?.Address != null)
                requestBody.Message.CcRecipients = [new() { EmailAddress = new EmailAddress { Address = sender.Address, Name = sender.Name } }];

            ResilienceContext resilienceContext = ResilienceContextPool.Shared.Get(message.MessageId, cancellationToken);
            try
            {
                await resiliencePipelineProvider.GetPipeline("graph-send").ExecuteAsync(async ctx =>
                    await graphClient.Users[notificationSenderUser.UserPrincipalName].SendMail.PostAsync(requestBody, cancellationToken: ctx.CancellationToken),
                    resilienceContext);
            }
            catch (Exception ex)
            {
                LogGraphSendFailed(ex, sender?.Address ?? "unknown");
            }
            finally
            {
                ResilienceContextPool.Shared.Return(resilienceContext);
            }
        }

        private static string BuildHtmlBody(string reason, MimeMessage message, ResolvedSender? sender)
        {
            string senderDisplay = sender?.Address != null
                ? $"{System.Net.WebUtility.HtmlEncode(sender.Name ?? sender.Address)} &lt;{System.Net.WebUtility.HtmlEncode(sender.Address)}&gt;"
                : "<em style=\"color:#8B7040;\">Unknown</em>";

            string toRecipients = message.To.Count > 0
                ? System.Net.WebUtility.HtmlEncode(string.Join(", ", message.To.Select(a => a.ToString())))
                : "<em style=\"color:#8B7040;\">None</em>";

            string ccRecipients = message.Cc.Count > 0
                ? System.Net.WebUtility.HtmlEncode(string.Join(", ", message.Cc.Select(a => a.ToString())))
                : "<em style=\"color:#8B7040;\">None</em>";

            string subject = System.Net.WebUtility.HtmlEncode(message.Subject ?? "(no subject)");
            string reasonEncoded = System.Net.WebUtility.HtmlEncode(reason);
            string timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

            return $"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                  <meta charset="UTF-8">
                  <meta name="viewport" content="width=device-width,initial-scale=1">
                  <link href="https://fonts.googleapis.com/css2?family=Quicksand:wght@300;400;500&display=swap" rel="stylesheet">
                </head>
                <body style="margin:0;padding:0;font-family:'Quicksand',sans-serif;font-weight:400;color:#2a2416;">

                  <!-- Outer wrapper -->
                  <table width="100%" cellpadding="0" cellspacing="0" style="padding:32px 16px;">
                    <tr><td align="center">
                      <table width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background-color:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(58,50,32,0.10);">

                        <!-- Header -->
                        <tr>
                          <td style="padding:28px 36px;text-align:center;">
                            <img src="https://raw.githubusercontent.com/bechoforest/MustMail/refs/heads/main/.images/logo-transparent.png"> </img>
                          </td>
                        </tr>

                        <!-- Gold accent bar -->
                        <tr>
                          <td style="height:3px;background:linear-gradient(90deg,#C9A84A,#D4AF50,#8B7040);"></td>
                        </tr>

                        <!-- Failure badge -->
                        <tr>
                          <td style="padding:32px 36px 0 36px;">
                            <table cellpadding="0" cellspacing="0" style="background-color:#fdf6e3;border:1px solid #C9A84A;border-left:4px solid #C9A84A;border-radius:4px;width:100%;">
                              <tr>
                                <td style="padding:14px 18px;">
                                  <p style="margin:0 0 4px 0;font-size:11px;letter-spacing:1.5px;text-transform:uppercase;color:#8B7040;font-weight:500;">Delivery failure reason</p>
                                  <p style="margin:0;font-size:15px;color:#2a2416;font-weight:500;">{reasonEncoded}</p>
                                </td>
                              </tr>
                            </table>
                          </td>
                        </tr>

                        <!-- Details table -->
                        <tr>
                          <td style="padding:24px 36px 32px 36px;">
                            <table cellpadding="0" cellspacing="0" style="width:100%;border-collapse:collapse;">
                              <tr>
                                <td style="padding:10px 0;border-bottom:1px solid #e8e3d8;width:90px;font-size:11px;letter-spacing:1.2px;text-transform:uppercase;color:#8B7040;font-weight:500;vertical-align:top;">Time</td>
                                <td style="padding:10px 0 10px 16px;border-bottom:1px solid #e8e3d8;font-size:14px;color:#2a2416;">{timestamp}</td>
                              </tr>
                              <tr>
                                <td style="padding:10px 0;border-bottom:1px solid #e8e3d8;font-size:11px;letter-spacing:1.2px;text-transform:uppercase;color:#8B7040;font-weight:500;vertical-align:top;">Subject</td>
                                <td style="padding:10px 0 10px 16px;border-bottom:1px solid #e8e3d8;font-size:14px;color:#2a2416;">{subject}</td>
                              </tr>
                              <tr>
                                <td style="padding:10px 0;border-bottom:1px solid #e8e3d8;font-size:11px;letter-spacing:1.2px;text-transform:uppercase;color:#8B7040;font-weight:500;vertical-align:top;">From</td>
                                <td style="padding:10px 0 10px 16px;border-bottom:1px solid #e8e3d8;font-size:14px;color:#2a2416;">{senderDisplay}</td>
                              </tr>
                              <tr>
                                <td style="padding:10px 0;border-bottom:1px solid #e8e3d8;font-size:11px;letter-spacing:1.2px;text-transform:uppercase;color:#8B7040;font-weight:500;vertical-align:top;">To</td>
                                <td style="padding:10px 0 10px 16px;border-bottom:1px solid #e8e3d8;font-size:14px;color:#2a2416;">{toRecipients}</td>
                              </tr>
                              <tr>
                                <td style="padding:10px 0;font-size:11px;letter-spacing:1.2px;text-transform:uppercase;color:#8B7040;font-weight:500;vertical-align:top;">CC</td>
                                <td style="padding:10px 0 10px 16px;font-size:14px;color:#2a2416;">{ccRecipients}</td>
                              </tr>
                            </table>
                          </td>
                        </tr>

                        <!-- Footer -->
                        <tr>
                          <td style="background-color:#e1dfd3;padding:16px 36px;text-align:center;">
                            <p style="margin:0;font-size:12px;color:#6b6352;">
                              Sent via self‑hosted <a href="https://mustmail.net" style="color:#C9A84A;text-decoration:none;">MustMail</a>
                            </p>
                          </td>
                        </tr>

                      </table>
                    </td></tr>
                  </table>

                </body>
                </html>
                """;
        }

        [LoggerMessage(EventId = 1200, Level = LogLevel.Error, Message = "Failed to send error notification email via Microsoft Graph for sender {Sender}")]
        private partial void LogGraphSendFailed(Exception exception, string sender);

        [LoggerMessage(EventId = 1201, Level = LogLevel.Error, Message = "Notification sender address '{Address}' was not found in the tenant — error notification not sent")]
        private partial void LogNotificationSenderNotFound(string address);
    }
}
