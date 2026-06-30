using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MustMail.App.Services.MailProcessing;

public partial class GraphUserLookupService(ILogger<GraphUserLookupService> logger, GraphServiceClient graphClient)
{
    public async Task<Microsoft.Graph.Models.User?> FindSenderUserAsync(
        string source,
        string senderAddress)
    {
        // Query graph for a user with the mail address, UPN, or alias matching the sender address
        UserCollectionResponse? users = await graphClient.Users
            .GetAsync(requestConfiguration => {
                string escapedSenderAddress = senderAddress.Replace("'", "''");

                requestConfiguration.QueryParameters.Filter =
                    $"mail eq '{escapedSenderAddress}' or " +
                    $"userPrincipalName eq '{escapedSenderAddress}' or " +
                    $"proxyAddresses/any(x:x eq 'smtp:{escapedSenderAddress}')";
            });

        // If there are no results then a user was not found
        if (users?.Value == null || users.Value.Count == 0)
        {
            LogUserNotFound(source, senderAddress);
            return null;
        }

        // If there were more than 1 result then multiple users were found
        if (users.Value.Count > 1)
        {
            LogMultipleUsersFound(
                                  senderAddress,
                                  users.Value
                                      .Select(u => u.UserPrincipalName)
                                      .OfType<string>());
        }

        Microsoft.Graph.Models.User user = users.Value.First();

        // Check if the user has a mailbox
        if (user.Mail == null && user.UserPrincipalName == null)
        {
            LogSenderTenantNoMailbox(source, senderAddress);
            return null;
        }

        // Check if the user has mailbox settings, this is a warning as it's possible for a user to not have any mailbox settings
        if (user.MailboxSettings == null)
        {
            LogSenderMailboxSettingsMissing(source, senderAddress);
        }

        LogUsingSender(source, senderAddress, user.DisplayName);

        return user;

    }
    // 1130s = GraphUserLookupService
    
    [LoggerMessage(
                      EventId = 1130,
                      Level = LogLevel.Warning,
                      Message = "Could not find a user with mail, userPrincipalName, or alias {Sender} for {Source}.")]
    private partial void LogUserNotFound(string source, string sender);

    [LoggerMessage(
                      EventId = 1131,
                      Level = LogLevel.Warning,
                      Message = "Multiple users found for sender {Sender}: {Users}. Using the first.")]
    private partial void LogMultipleUsersFound(string sender, IEnumerable<string> users);

    [LoggerMessage(EventId = 1132, Level = LogLevel.Information, Message = "Sender {Sender} ({DisplayName}) verified for {Source}")]
    private partial void LogUsingSender(string source, string sender, string? displayName);

    [LoggerMessage(EventId = 1133, Level = LogLevel.Error, Message = "{Source} address {Sender} has no mailbox configured in the tenant")]
    private partial void LogSenderTenantNoMailbox(string source, string sender);

    [LoggerMessage(EventId = 1134, Level = LogLevel.Warning, Message = "Mailbox settings missing for {Source} address {Sender}")]
    private partial void LogSenderMailboxSettingsMissing(string source, string sender);
}