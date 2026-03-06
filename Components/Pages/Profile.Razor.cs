using Microsoft.AspNetCore.Components.Authorization;
using MustMail.Db;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MustMail.Components.Pages
{
    public class ProfileBase : ComponentBase
    {
        // Class variables
        private readonly List<TimeZoneInfo> _timeZones = [.. TimeZoneInfo.GetSystemTimeZones()];

        // Page variables
        protected readonly ProfileForm Model = new();

        // Component parameters and dependency injection
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private AuthenticationStateProvider AuthenticationState { get; set; } = default!;
        [Inject] private IDbContextFactory<DatabaseContext> DbFactory { get; set; } = default!;
        [CascadingParameter] private Action<string>? SetTitle { get; set; }

        // Lifecycle method called after parameters and property values are set
        protected override async Task OnInitializedAsync()
        {
            // Set page title
            SetTitle?.Invoke("Profile");

            using DatabaseContext dbContext = DbFactory.CreateDbContext();

            AuthenticationState authState = await AuthenticationState.GetAuthenticationStateAsync();

            // Get user
            User? user = await dbContext.User.FindAsync(authState.User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (user == null)
                return;

            // Load user's profile
            await dbContext.Entry(user)
                .Reference(u => u.Profile)
                .LoadAsync();

            // Update from with the current user's profile
            Model.TimeZone = TimeZoneInfo.FindSystemTimeZoneById(user.Profile.TimeZone);
            Model.DateFormat = user.Profile.DateFormat;
            Model.TimeFormat = user.Profile.TimeFormat;
        }

        // Valid form submit - update user's profile in the database
        protected async Task OnValidSubmit()
        {
            using DatabaseContext dbContext = DbFactory.CreateDbContext();

            AuthenticationState authState = await AuthenticationState.GetAuthenticationStateAsync();

            // Get user
            User? user = await dbContext.User.FindAsync(authState.User.FindFirstValue(ClaimTypes.NameIdentifier));
            if (user == null)
                return;

            // Load user's profile
            await dbContext.Entry(user)
                .Reference(u => u.Profile)
                .LoadAsync();

            dbContext.Entry(user.Profile).CurrentValues.SetValues(new Models.Profile
            { DateFormat = Model.DateFormat, TimeFormat = Model.TimeFormat, TimeZone = Model.TimeZone.Id });

            if (dbContext.Entry(user.Profile).Properties.Any(p => p.IsModified))
            {
                _ = await dbContext.SaveChangesAsync();
            }

            _ = Snackbar.Add("Your profile has been updated!", Severity.Success);

            // Update model from profile
            Model.TimeZone = TimeZoneInfo.FindSystemTimeZoneById(user.Profile.TimeZone);
            Model.DateFormat = user.Profile.DateFormat;
            Model.TimeFormat = user.Profile.TimeFormat;

            // Re-render component
            await InvokeAsync(StateHasChanged);
        }

        // Time zone search - searches all .NET time ones by display name
        protected Task<IEnumerable<TimeZoneInfo>> TimeZoneSearch(string value, CancellationToken _)
        {
            return string.IsNullOrEmpty(value)
                ? Task.FromResult<IEnumerable<TimeZoneInfo>>(_timeZones)
                : Task.FromResult(_timeZones.Where(x =>
                x.DisplayName.Contains(value, StringComparison.InvariantCultureIgnoreCase)));
        }

        // Form
        protected class ProfileForm
        {
            [Required] public TimeZoneInfo TimeZone { get; set; } = null!;

            [Required] public string DateFormat { get; set; } = null!;

            [Required] public string TimeFormat { get; set; } = null!;

        }
    }
}
