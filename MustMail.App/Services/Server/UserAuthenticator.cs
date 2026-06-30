using System.Net;
using System.Threading.RateLimiting;
using Isopoh.Cryptography.Argon2;
using SmtpServer;
using SmtpServer.Authentication;

namespace MustMail.App.Services.Server;

public partial class UserAuthenticator(ILogger<UserAuthenticator> logger, IDbContextFactory<DatabaseContext> dbFactory) : IUserAuthenticator
{
    private static readonly PartitionedRateLimiter<string> _ipRateLimiter = PartitionedRateLimiter.Create<string, string>(
        ip => RateLimitPartition.GetSlidingWindowLimiter(ip, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(10),
            SegmentsPerWindow = 10,
            QueueProcessingOrder = QueueProcessingOrder.NewestFirst,
            QueueLimit = 0,
        }));

    private static readonly PartitionedRateLimiter<string> _userRateLimiter = PartitionedRateLimiter.Create<string, string>(
      user => RateLimitPartition.GetSlidingWindowLimiter(user, _ => new SlidingWindowRateLimiterOptions
      {
          PermitLimit = 5,
          Window = TimeSpan.FromMinutes(15),
          SegmentsPerWindow = 15,
          QueueProcessingOrder = QueueProcessingOrder.NewestFirst,
          QueueLimit = 0,
      }));

    public async Task<bool> AuthenticateAsync(ISessionContext session, string user, string password, CancellationToken cancellationToken)
    {
        IPEndPoint endPoint = (IPEndPoint)session.Properties["EndpointListener:RemoteEndPoint"];
        string ipAddress = endPoint.Address.ToString();

        await using DatabaseContext dbContext = await dbFactory.CreateDbContextAsync(cancellationToken);

        SMTPAccount? account = await dbContext.SMTPAccount
            .SingleOrDefaultAsync(a => a.Username == user, cancellationToken);

        // Unknown username
        if (account is null)
        {
       
            RateLimitLease unknownUserLease = _ipRateLimiter.AttemptAcquire(ipAddress);
            if (!unknownUserLease.IsAcquired)
            {
                LogUserRateLimited(user, ipAddress);
                return false;
            }

            LogAuthUnknownUser(user, ipAddress);
            return false;
        }

        // Bad password
        if (!Argon2.Verify(account.Password, password))
        {
            RateLimitLease ipLease = _ipRateLimiter.AttemptAcquire(ipAddress);
            RateLimitLease userLease = _userRateLimiter.AttemptAcquire(account.Username);

            if (!ipLease.IsAcquired)
            {
                LogIPRateLimited(account.Username, ipAddress);
                
                return false;
            }

            if (!userLease.IsAcquired)
            {
                LogUserRateLimited(user, ipAddress);
                return false;
            }

            LogAuthInvalidPassword(user, ipAddress);
            return false;
        }

        LogAuthSucceeded(user, ipAddress);
        return true;
    }

    public IUserAuthenticator CreateInstance(ISessionContext _) => new UserAuthenticator(logger, dbFactory);

    // 1200s = UserAuthenticator
    [LoggerMessage(
     EventId = 1201,
     Level = LogLevel.Warning,
     Message = "SMTP authentication blocked by user rate limit: user {User} from {IpAddress}")]
    private partial void LogUserRateLimited(string user, string ipAddress);

    [LoggerMessage(
        EventId = 1202,
        Level = LogLevel.Warning,
        Message = "SMTP authentication blocked by IP rate limit: user {User} from {IpAddress}")]
    private partial void LogIPRateLimited(string user, string ipAddress);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Warning,
        Message = "SMTP authentication failed: unknown user {User} from {IpAddress}")]
    private partial void LogAuthUnknownUser(string user, string ipAddress);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Warning,
        Message = "SMTP authentication failed: invalid password for user {User} from {IpAddress}")]
    private partial void LogAuthInvalidPassword(string user, string ipAddress);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Debug,
        Message = "SMTP authentication succeeded: user {User} from {IpAddress}")]
    private partial void LogAuthSucceeded(string user, string ipAddress);

   
}