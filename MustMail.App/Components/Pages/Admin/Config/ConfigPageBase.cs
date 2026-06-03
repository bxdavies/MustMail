using KellermanSoftware.CompareNetObjects;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.Graph.Models;
using Microsoft.JSInterop;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MustMail.App.Components.Pages.Admin.Config;

public partial class ConfigPageBase : ComponentBase
{
    protected MudForm? SettingsForm;
    protected Configuration Configuration = null!;

    private IDisposable? registration;

    [Inject] public IJSRuntime JS { get; set; } = null!;
    [Inject] public ISnackbar Snackbar { get; set; } = null!;
    [Inject] public IConfiguration RawConfiguration { get; set; } = null!;
    [Inject] public NavigationManager Navigation {  get; set; } = null!;
    [Inject] public IDialogService DialogService { get; set; } = null!;

    private async ValueTask OnLocationChanging(LocationChangingContext context)
    {
        CompareLogic compareLogic = new(new ComparisonConfig { MaxDifferences = 10 });
        ComparisonResult result = compareLogic.Compare(Configuration, RawConfiguration.Get<Configuration>());

        if (!result.AreEqual)
        {
            bool confirmed = await DialogService.ShowMessageBoxAsync(
                "Unsaved Changes",
                "You have unsaved changes that will be lost. Are you sure you want to leave this page?",
                yesText: "Leave",
                cancelText: "Stay") ?? false;

            if (!confirmed)
            {
                context.PreventNavigation();
            }
        }
    }

    protected override void OnInitialized()
    {
        Configuration = RawConfiguration.Get<Configuration>()!;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            registration =
                Navigation.RegisterLocationChangingHandler(OnLocationChanging);
        }
    }

    protected async Task ValidateAndSave()
    {
        if (SettingsForm is null) return;

        await SettingsForm.ValidateAsync();

        if (!SettingsForm.IsValid)
        {
            _ = Snackbar.Add("Fix validation errors before saving.", Severity.Error);
            return;
        }

        await File.WriteAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Data", "appsettings.json"),
            JsonSerializer.Serialize(Configuration, JsonWriteDefaults.Options));

        _ = Snackbar.Add("Settings saved.", Severity.Success);
    }

    protected async Task CopyToClipboard(string value)
    {
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", value);
        _ = Snackbar.Add("Environment variable copied", Severity.Success);
    }

    public void Dispose() => registration?.Dispose();

    [GeneratedRegex(@"^(\*|[A-Za-z0-9._%+-]+)@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$")]
    protected static partial Regex AllowedEmailRegex();
}
