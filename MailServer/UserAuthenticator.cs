using Isopoh.Cryptography.Argon2;
using MustMail.Db;
using SmtpServer;
using SmtpServer.Authentication;

namespace MustMail.MailServer;

public partial class UserAuthenticator(ILogger<UserAuthenticator> logger, IDbContextFactory<DatabaseContext> dbFactory) : IUserAuthenticator
{
    public async Task<bool> AuthenticateAsync(ISessionContext _, string user, string password, CancellationToken cancellationToken)
    {
        LogAuthAttempt(user);

        await using DatabaseContext dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);

        SMTPAccount? account = await dbContext.SMTPAccount
            .SingleOrDefaultAsync(a => a.Name == user, cancellationToken: cancellationToken);

        if (account is null)
        {
            LogAuthUnknownUser(user);
            return false;
        }


        // Verify the password hashes
        bool ok = Argon2.Verify(account.Password, password);

        if (!ok)
        {
            LogAuthInvalidPassword(user);
            return false;
        }

        LogAuthSucceeded(user);
        return true;
    }

    public IUserAuthenticator CreateInstance(ISessionContext _)
    {
        return new UserAuthenticator(logger, dbFactory);
    }

    // 1200s = UserAuthenticator

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Debug,
        Message = "SMTP authentication attempt for user {User}")]
    private partial void LogAuthAttempt(string user);

    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Warning,
        Message = "SMTP authentication failed: unknown user {User}")]
    private partial void LogAuthUnknownUser(string user);

    [LoggerMessage(
        EventId = 1203,
        Level = LogLevel.Warning,
        Message = "SMTP authentication failed: invalid password for user {User}")]
    private partial void LogAuthInvalidPassword(string user);

    [LoggerMessage(
        EventId = 1204,
        Level = LogLevel.Information,
        Message = "SMTP authentication succeeded for user {User}")]
    private partial void LogAuthSucceeded(string user);
}