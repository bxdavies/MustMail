using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MustMail.MailServer;

public partial class GraphUserHelper(ILogger<MessageHandler> logger, GraphServiceClient graphClient)
{
    public async Task<Microsoft.Graph.Models.User?> FindSenderUserAsync(
string headerType,
string senderAddress,
CancellationToken cancellationToken)
    {
        // Query graph for a user with the mail address, UPN, or alias matching the sender address
        UserCollectionResponse? users = await graphClient.Users
            .GetAsync(requestConfiguration =>
            {
                string escapedSenderAddress = senderAddress.Replace("'", "''");

                requestConfiguration.QueryParameters.Filter =
                    $"mail eq '{escapedSenderAddress}' or " +
                    $"userPrincipalName eq '{escapedSenderAddress}' or " +
                    $"proxyAddresses/any(x:x eq 'smtp:{escapedSenderAddress}')";
            }, cancellationToken);

        // If there are no results then a user was not found
        if (users?.Value == null || users.Value.Count == 0)
        {
            LogUserNotFound(headerType, senderAddress);
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
            LogSenderTenantNoMailbox(headerType, senderAddress);
            return null;
        }

        // Check if the user has mailbox settings, this is a warning as it's possible for a user to not have any mailbox settings
        if (user.MailboxSettings == null)
        {
            LogSenderMailboxSettingsMissing(headerType, senderAddress);
        }

        LogUsingSender(headerType, senderAddress, user.DisplayName);

        return user;

    }

    [LoggerMessage(
        EventId = 4007,
        Level = LogLevel.Warning,
        Message = "Could not find a user with mail, userPrincipalName, or alias {Sender} for {HeaderType}.")]
    private partial void LogUserNotFound(string headerType, string sender);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Multiple users found for sender {Sender}: {Users}. Using the first.")]
    private partial void LogMultipleUsersFound(string sender, IEnumerable<string> users);

    [LoggerMessage(EventId = 1108, Level = LogLevel.Information, Message = "Using {HeaderType} sender {Sender} ({DisplayName}) for outgoing email")]
    private partial void LogUsingSender(string headerType, string sender, string? displayName);

    [LoggerMessage(EventId = 1114, Level = LogLevel.Error, Message = "{HeaderType} address {Sender} has no mailbox configured in the tenant")]
    private partial void LogSenderTenantNoMailbox(string headerType, string sender);

    [LoggerMessage(EventId = 1115, Level = LogLevel.Warning, Message = "Mailbox settings missing for {HeaderType} address {Sender}")]
    private partial void LogSenderMailboxSettingsMissing(string headerType, string sender);
}