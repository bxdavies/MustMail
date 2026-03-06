using Ganss.Xss;
using Microsoft.AspNetCore.Components.Authorization;
using MimeKit;
using MudBlazor.Extensions;
using MustMail.Db;
using MustMail.MailServer;
using System.Security.Claims;

namespace MustMail.Components.Pages;

public class HomeBase : ComponentBase
{
    // Class variables
    private Models.Profile? _profile;
    private IDisposable? _subscription;

    // Page variables
    protected string? UserId;
    protected string Name = "";
    protected int MessageCount = 0;
    protected string MostRecentEmailSubject = "Unknown";
    protected DateTime MostRecentEmailTimestamp = DateTime.MinValue;
    protected List<Message> Messages = [];
    protected MudTabs MessageTabs = default!;
    protected MimeMessage? ActiveMessage;
    protected bool StoreEmails;


    // Component parameters and dependency injection
    [Inject] private AuthenticationStateProvider AuthenticationState { get; set; } = default!;
    [Inject] private IDbContextFactory<DatabaseContext> DbFactory { get; set; } = default!;
    [Inject] private UpdateService Updates { get; set; } = default!;
    [Inject] public IConfiguration Configuration { get; set; } = default!;
    [CascadingParameter] private Action<string>? SetTitle { get; set; }

    // Lifecycle method called after parameters and property values are set
    protected override async Task OnInitializedAsync()
    {
        // Set page title
        SetTitle?.Invoke("Home");

        StoreEmails = Configuration.Get<Configuration>()!.MustMail.StoreEmails;

        AuthenticationState authState = await AuthenticationState.GetAuthenticationStateAsync();

        // Set name
        Name = authState?.User?.Identity?.Name ?? "";

        using DatabaseContext dbContext = DbFactory.CreateDbContext();

        // Get user
        User? user = await dbContext.User.FindAsync(authState?.User.FindFirstValue(ClaimTypes.NameIdentifier));
        if (user == null)
        {
            return;
        }

        await dbContext.Entry(user).Reference(u => u.Profile).LoadAsync();

        UserId = user.Id;
        _profile = user.Profile;



        if (StoreEmails)
        {



            // Subscribe to events for the current user id using the UpdateServer
            _subscription = Updates.Subscribe(UserId, async () =>
            {
                await GetMessages();
                await InvokeAsync(StateHasChanged);
            });

            await GetMessages();
        }


    }

    // Get messages - get's the users messages from the database and gathers some details about the most recent message 
    protected async Task GetMessages()
    {
        using DatabaseContext dbContext = DbFactory.CreateDbContext();

        // Get user including messages
        User user = await dbContext.User.Include(u => u.Messages).SingleAsync(u => u.Id == UserId);

        // Set message count
        MessageCount = user.Messages.Count;

        // If there are messages get the most recent one
        if (MessageCount > 0)
        {
            // Get the most recent message 
            Message? mostRecentMessage = user.Messages.OrderByDescending(m => m.Timestamp).First();
            if (mostRecentMessage == null)
            {
                return;
            }

            // Grab details for panel 
            MostRecentEmailTimestamp = mostRecentMessage.Timestamp;
            MostRecentEmailSubject = mostRecentMessage.Subject;

            // Create file path
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "maildrop",
                user.Id,
                $"{mostRecentMessage.Id}.eml");

            // Sanitize file path
            path = Helpers.SanitizeFilePath(path);

            // Load the eml file
            ActiveMessage = MimeMessage.Load(path);

        }

        // All messages
        Messages = [.. user.Messages.OrderByDescending(m => m.Timestamp)];

    }

    // Sanitized body - Prevent Cross-site scripting (XSS) by Sanitizing html string
    protected string SanitizedBody(string html)
    {
        HtmlSanitizer sanitizer = new();

        // Allow inline images
        _ = sanitizer.AllowedSchemes.Add("data");

        sanitizer.FilterUrl += (sender, e) =>
        {
            if (e.OriginalUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return;

            e.SanitizedUrl = null;
        };

        return sanitizer.Sanitize(html);
    }

    protected string DateTimeConvertAndFormat(DateTime dateTime)
    {
        if (_profile == null)
            return dateTime.ToIsoDateString();

        // Get the user's time zone
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(_profile.TimeZone);

        return TimeZoneInfo.ConvertTimeFromUtc(dateTime, timeZone).ToString($"{_profile.DateFormat} {_profile.TimeFormat}");
    }

    // Format friendly date - style date
    public string FormatFriendlyDate(DateTime dateTime)
    {
        DateTime now = DateTime.Now;
        DateTime today = now.Date;
        DateTime inputDate = dateTime.Date;
        TimeZoneInfo timeZone = TimeZoneInfo.Utc;

        if (_profile != null)
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(_profile.TimeZone);
        }

        // Today → time only with AM/PM
        if (inputDate == today)
        {
            return _profile == null
                ? dateTime.ToString("h:mm tt")
                : TimeZoneInfo.ConvertTimeFromUtc(dateTime, timeZone).ToString(_profile.TimeFormat);
        }

        // Last 5 days (excluding today)
        if (inputDate >= today.AddDays(-5))
        {
            return _profile == null
                ? dateTime.ToString("ddd d/M")
                : TimeZoneInfo.ConvertTimeFromUtc(dateTime, timeZone).ToString(_profile.DateFormat.Replace("y", ""));
        }

        // Older than 5 days
        return _profile == null
            ? dateTime.ToString("d/M/yy")
            : TimeZoneInfo.ConvertTimeFromUtc(dateTime, timeZone).ToString(_profile.DateFormat);
    }

    // Message changed - When tab changed, get the message id and load the eml file
    protected void MessageChanged(int index)
    {
        if (index < 0 && index > (MessageTabs.Panels.Count - 1))
            index = 1;

        if (MessageTabs.Panels[index].ID is not string messageId)
            return;

        if (UserId == null)
            return;

        // Create file path
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "maildrop",
            UserId,
            $"{messageId}.eml");

        // Sanitize file path
        path = Helpers.SanitizeFilePath(path);

        // Load the eml file
        ActiveMessage = MimeMessage.Load(path);


    }

    protected static string FormatAttachmentMeta(MimePart part)
    {
        // ContentType like "image/png"
        string type = part.ContentType?.MimeType ?? "file";

        // Size if known (may be null/unknown depending on how you stored it)
        // MimeKit sometimes has ContentDisposition?.Size, or you may track size elsewhere.
        long? size = part.ContentDisposition?.Size;
        return size is null ? type : $"{type} • {FormatBytes(size.Value)}";
    }

    protected static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }

    // Dispose - Unsubscribe from events
    public void Dispose()
    {
        _subscription?.Dispose();
    }
}
