using Microsoft.EntityFrameworkCore;

namespace MustMail.App.Components.Pages.Admin;

public class DashboardBase : ComponentBase
{
    protected int UserCount;
    protected int AdminCount;
    protected int SmtpAccountCount;
    protected int EmailCount;
    protected DateTime MostRecentMessage;
    protected string LogLevel = "Information";
    protected bool StoreMail;
    protected int RetentionDays;
    protected string SmtpHost = "localhost";
    protected bool AllowInsecure;

    [Inject] public IDbContextFactory<DatabaseContext> DbFactory { get; set; } = null!;
    [Inject] public IConfiguration Configuration { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        Configuration config = Configuration.Get<Configuration>()!;
        LogLevel = config.Serilog.MinimumLevel.Default;
        StoreMail = config.Mail.StoreMail;
        RetentionDays = config.Mail.RetentionDays;
        SmtpHost = config.Smtp.Host;
        AllowInsecure = config.Smtp.AllowInsecure;

        await using DatabaseContext dbContext = await DbFactory.CreateDbContextAsync();
        UserCount = await dbContext.User.CountAsync();
        AdminCount = await dbContext.User.CountAsync(u => u.Admin);
        SmtpAccountCount = await dbContext.SMTPAccount.CountAsync();
        EmailCount = await dbContext.Message.CountAsync();

        Message? message = await dbContext.Message
            .OrderByDescending(m => m.Timestamp)
            .FirstOrDefaultAsync();

        MostRecentMessage = message?.Timestamp ?? DateTime.MinValue;
    }

    protected static string FormatRelativeTime(DateTime utc)
    {
        TimeSpan diff = DateTime.UtcNow - utc;
        return diff.TotalMinutes < 1 ? "Just now"
            : diff.TotalHours < 1 ? $"{(int)diff.TotalMinutes}m ago"
            : diff.TotalDays < 1 ? $"{(int)diff.TotalHours}h ago"
            : diff.TotalDays < 7 ? $"{(int)diff.TotalDays}d ago"
            : utc.ToString("dd MMM yyyy");
    }
}
