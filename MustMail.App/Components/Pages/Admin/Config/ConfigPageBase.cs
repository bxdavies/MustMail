using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.JSInterop;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MustMail.App.Components.Pages.Admin.Config;

public partial class ConfigPageBase : ComponentBase
{
    protected MudForm? SettingsForm;
    protected Configuration Configuration = null!;

    [Inject] public IJSRuntime JS { get; set; } = null!;
    [Inject] public ISnackbar Snackbar { get; set; } = null!;
    [Inject] public IConfiguration RawConfiguration { get; set; } = null!;

    protected override void OnInitialized()
    {
        Configuration = RawConfiguration.Get<Configuration>()!;
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

    [GeneratedRegex(@"^(\*|[A-Za-z0-9._%+-]+)@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$")]
    protected static partial Regex AllowedEmailRegex();
}
