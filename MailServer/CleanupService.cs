using Microsoft.Extensions.Options;
using MustMail.Db;
using Quartz;

namespace MustMail.MailServer
{
    public partial class CleanupService(IOptions<Configuration> options,
        ILogger<CleanupService> logger, IDbContextFactory<DatabaseContext> dbFactory, UpdateService updates) : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {

            LogCleanupStarted();

            await using DatabaseContext dbContext = await dbFactory.CreateDbContextAsync();

            DateTime expiryDate = DateTime.Now - TimeSpan.FromDays(options.Value.MustMail.RetentionDays);
            LogExpiryCutoff(options.Value.MustMail.RetentionDays, expiryDate);

            while (true)
            {
                // Find a userId that has expired messages
                string? userId = await dbContext.Message
                    .Where(m => m.Timestamp < expiryDate)
                    .Select(m => m.UserId)
                    .Distinct()
                    .FirstOrDefaultAsync();

                if (userId == null)
                    break;

                LogProcessingUser(userId);

                // Load the messages for that user that are expired 
                var expired = await dbContext.Message
                    .Where(m => m.UserId == userId && m.Timestamp < expiryDate)
                    .Select(m => new { m.Id })
                    .ToListAsync();

                // Remove the .eml files
                foreach (var m in expired)
                {
                    // Build path
                    string path = Path.Combine(
                        AppContext.BaseDirectory,
                        "maildrop",
                        userId,
                        $"{m.Id}.eml");

                    // Build path
                    string attachmentsPath = Path.Combine(
                        AppContext.BaseDirectory,
                        "maildrop",
                        userId,
                        $"{m.Id}.eml");

                    // Delete the file if it exists 
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                        LogFileDeleted(path);
                    }

                    // Delete the file if it exists 
                    if (File.Exists(attachmentsPath))
                    {
                        File.Delete(path);
                        LogAttachmentFolderDeleted(path);
                    }

                    // Get the user's directory
                    string? directory = Path.GetDirectoryName(path);

                    if (Directory.Exists(directory))
                    {
                        // If the directory is empty, delete it
                        if (!Directory.EnumerateFileSystemEntries(directory).Any())
                        {
                            Directory.Delete(directory);
                            LogUserFolderRemoved(userId);
                        }
                    }
                }

                // Remove expired messages from the database
                List<string> ids = [.. expired.Select(x => x.Id)];
                _ = await dbContext.Message
                    .Where(m => ids.Contains(m.Id))
                    .ExecuteDeleteAsync();

                LogMessagesDeleted(ids.Count, userId);

                // Trigger update service to update any clients
                LogNotifyClients(userId);
                await updates.NewMessageForUserAsync(userId);
            }
        }

        [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Starting mailbox cleanup")]
        private partial void LogCleanupStarted();

        [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Mailbox cleanup retention is {RetentionDays} days. Expiry cutoff is {ExpiryDate}")]
        private partial void LogExpiryCutoff(int retentionDays, DateTime expiryDate);

        [LoggerMessage(
            EventId = 2003,
            Level = LogLevel.Information,
            Message = "Processing expired messages for user {UserId}")]
        private partial void LogProcessingUser(string userId);

        [LoggerMessage(
            EventId = 2004,
            Level = LogLevel.Debug,
            Message = "Deleted message file {Path}")]
        private partial void LogFileDeleted(string path);

        [LoggerMessage(
          EventId = 2005,
          Level = LogLevel.Debug,
          Message = "Deleted attachment folder {Path}")]
        private partial void LogAttachmentFolderDeleted(string path);

        [LoggerMessage(
            EventId = 2006,
            Level = LogLevel.Information,
            Message = "Removed user {UserId} maildrop folder because it was empty")]
        private partial void LogUserFolderRemoved(string userId);

        [LoggerMessage(
            EventId = 2007,
            Level = LogLevel.Information,
            Message = "Deleted {Count} expired messages from database for user {UserId}")]
        private partial void LogMessagesDeleted(int count, string userId);

        [LoggerMessage(
            EventId = 2008,
            Level = LogLevel.Debug,
            Message = "Notifying clients of mailbox update for user {UserId}")]
        private partial void LogNotifyClients(string userId);
    }
}
