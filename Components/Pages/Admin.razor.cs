using Isopoh.Cryptography.Argon2;
using Microsoft.JSInterop;
using System.Text.Json;

namespace MustMail.Components.Pages
{
    public class AdminBase : ComponentBase
    {
        // Page variables
        protected MudForm? SettingsForm;
        protected Configuration Config = null!;
        protected string NewAllowedFrom = "";
        protected string NewAllowedRecipients = "";

        protected List<User> Users = [];
        protected MudDataGrid<User> UserGrid = null!;

        protected List<SMTPAccount> SMTPAccounts = [];
        protected MudDataGrid<SMTPAccount> SMTPAccountGrid = null!;

        // Component parameters and dependency injection
        [Inject] public IJSRuntime JS { get; set; } = null!;
        [Inject] public ISnackbar Snackbar { get; set; } = null!;
        [Inject] public IDbContextFactory<DatabaseContext> DbFactory { get; set; } = null!;
        [Inject] public IConfiguration Configuration { get; set; } = null!;
        [CascadingParameter] private Action<string>? SetTitle { get; set; }

        // Lifecycle method called after parameters and property values are set
        protected override async Task OnInitializedAsync()
        {
            // Set page title
            SetTitle?.Invoke("Admin");

            await using DatabaseContext dbContext = await DbFactory.CreateDbContextAsync();

            Config = Configuration.Get<Configuration>()!;

            Users = await dbContext.User.ToListAsync();
            SMTPAccounts = await dbContext.SMTPAccount.ToListAsync();
        }

        // New SMTP account - start editing a new SMTP account in form modal
        protected async Task NewSMTPAccount()
        {
            await SMTPAccountGrid.SetEditingItemAsync(new SMTPAccount() { Username = "", Password = "", Description = "" });
        }

        // Remove SMTP account - remove account
        protected async Task RemoveSMTPAccount(SMTPAccount item)
        {
            await using DatabaseContext dbContext = await DbFactory.CreateDbContextAsync();

            _ = SMTPAccounts.Remove(item);

            _ = dbContext.SMTPAccount.Remove(item);
            _ = await dbContext.SaveChangesAsync();
        }

        // SMTP account item changes - called when creating or editing an SMTP account
        protected async Task<DataGridEditFormAction> SMTPAccountItemChanges(SMTPAccount item)
        {
            await using DatabaseContext dbContext = await DbFactory.CreateDbContextAsync();

            // New item
            if (item.Id == 0)
            {
                // Hash the password
                item.Password = Argon2.Hash(item.Password);

                // Add the account to the database
                _ = await dbContext.SMTPAccount.AddAsync(item);
                _ = await dbContext.SaveChangesAsync();

                // Add the account to the grid
                SMTPAccounts.Add(item);

                _ = Snackbar.Add($"SMTP Account added successfully!", Severity.Success);

                return DataGridEditFormAction.Close;
            }

            // Get item from database
            SMTPAccount? SMTPAccount = await dbContext.SMTPAccount.FindAsync(item.Id);

            if (SMTPAccount == null)
                return DataGridEditFormAction.Close;

            // If the user has updated the password we need to hash it
            if (item.Password != SMTPAccount.Password)
                item.Password = Argon2.Hash(item.Password);

            // Update values in DB
            dbContext.Entry(SMTPAccount).CurrentValues.SetValues(item);

            _ = await dbContext.SaveChangesAsync();

            _ = Snackbar.Add($"SMTP Account updated successfully!", Severity.Success);

            return DataGridEditFormAction.Close;
        }

        // Remove user - remove user but check there is at least one admin
        protected async Task RemoveUser(User item)
        {
            await using DatabaseContext dbContext = await DbFactory.CreateDbContextAsync();

            // At least one admin check
            int numberOfAdminUsers = await dbContext.User.Where(u => u.Admin == true).CountAsync();
            if (numberOfAdminUsers == 1 && item.Admin)
            {
                _ = Snackbar.Add("There must be at least one admin!", Severity.Error);
                return;
            }

            _ = Users.Remove(item);

            // Create file path
            string path = Path.Combine(
               AppContext.BaseDirectory,
               "maildrop",
               item.Id);

            // Remove users emails
            Directory.Delete(path);

            _ = dbContext.User.Remove(item);
            _ = await dbContext.SaveChangesAsync();
        }

        // User item changed - called when editing a user
        protected async Task<DataGridEditFormAction> UserItemChanges(User item)
        {
            await using DatabaseContext dbContext = await DbFactory.CreateDbContextAsync();

            User? user = await dbContext.User.FindAsync(item.Id);
            if (user == null)
                return DataGridEditFormAction.Close;

            // At least one admin check
            int numberOfAdminUsers = await dbContext.User.Where(u => u.Admin == true).CountAsync();
            if (numberOfAdminUsers == 1 && !item.Admin && user.Admin)
            {
                _ = Snackbar.Add("There must be at least one admin!", Severity.Error);
                item.Admin = true;
                return DataGridEditFormAction.Close;
            }

            // Update values in DB
            dbContext.Entry(user).CurrentValues.SetValues(item);

            _ = await dbContext.SaveChangesAsync();

            _ = Snackbar.Add($"User updated successfully!", Severity.Success);

            return DataGridEditFormAction.Close;
        }

        // Add allowed from - add new allowed from to config
        protected void AddAllowedFrom()
        {
            string v = (NewAllowedFrom).Trim();
            if (string.IsNullOrWhiteSpace(v)) return;

            Config.MustMail.AllowedSenders.Add(v);
            NewAllowedFrom = "";
        }

        // Remove allowed from - remove allowed from
        protected void RemoveAllowedFrom(string value)
        {
            _ = (Config.MustMail.AllowedSenders.Remove(value));
        }

        // Add allowed to - add new allowed to 
        protected void AddAllowedTo()
        {
            string v = (NewAllowedRecipients).Trim();
            if (string.IsNullOrWhiteSpace(v)) return;

            Config.MustMail.AllowedRecipients.Add(v);
            NewAllowedRecipients = "";
        }

        // Removed allowed to - remove allowed to from to config
        protected void RemoveAllowedTo(string value)
        {
            _ = (Config.MustMail.AllowedRecipients.Remove(value));
        }

        // Validate and save - validate from and save to appsettings.json
        protected async Task ValidateAndSave()
        {
            if (SettingsForm is null) return;

            await SettingsForm.ValidateAsync();

            if (!SettingsForm.IsValid)
            {
                _ = Snackbar.Add("Fix validation errors before saving.", Severity.Error);
                return;
            }

            await File.WriteAllTextAsync(@"appsettings.json", JsonSerializer.Serialize(Config, JsonDefaults.Options));

            _ = Snackbar.Add("Settings saved.", Severity.Success);
        }

        // Copy to clipboard - Using JavaScript copy the string to the clipboard
        protected async Task CopyToClipboard(string value)
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", value);
            _ = Snackbar.Add("Environment variable copied", Severity.Success);
        }
    }
}
