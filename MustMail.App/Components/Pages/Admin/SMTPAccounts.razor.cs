using Isopoh.Cryptography.Argon2;
using Microsoft.JSInterop;
using System.Text.Json;

namespace MustMail.App.Components.Pages.Admin;
public class SMTPAccountsBase : ComponentBase
{
    // Page variables
    protected List<SMTPAccount> SMTPAccounts = [];
    protected MudDataGrid<SMTPAccount> SMTPAccountGrid = null!;

    // Class variables
    private string _searchString = null!;

    // Component parameters and dependency injection
    [Inject] public ISnackbar Snackbar { get; set; } = null!;
    [Inject] public IDbContextFactory<DatabaseContext> DbFactory { get; set; } = null!;
    [Inject] public IConfiguration Configuration { get; set; } = null!;


    // Sever Reload - Used by mud data grid to load the data server side using pagnation and supporting search
    protected async Task<GridData<SMTPAccount>> ServerReload(GridState<SMTPAccount> state, CancellationToken token)
    {
        await using DatabaseContext dbContext = await DbFactory.CreateDbContextAsync(token);
        var query = dbContext.SMTPAccount.Include(a => a.AllowedSenders).Include(a => a.AllowedRecipients).AsSplitQuery().AsQueryable();

        if (!string.IsNullOrWhiteSpace(_searchString))
            query = query.Where(u => u.Username.Contains(_searchString));

        int total = await query.CountAsync(cancellationToken: token);

        List<SMTPAccount> items = await query
            .OrderBy(u => u.Id)
            .Skip(state.Page * state.PageSize)
            .Take(state.PageSize)
            .ToListAsync(cancellationToken: token);

        return new GridData<SMTPAccount> { Items = items, TotalItems = total };
    }

    // On Search - On search string changed reload the server data with the search string
    protected Task OnSearch(string text)
    {
        _searchString = text;
        return SMTPAccountGrid.ReloadServerData();
    }
    // New SMTP account - start editing a new SMTP account in form modal
    protected async Task NewSMTPAccount()
    {
        await SMTPAccountGrid.SetEditingItemAsync(new SMTPAccount
        {
            Username = "",
            Password = "",
            Description = ""
        });
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
        SMTPAccount? smtpAccount = await dbContext.SMTPAccount.Include(a => a.AllowedSenders).Include(a => a.AllowedRecipients).SingleAsync(a => a.Id == item.Id);

        if (smtpAccount == null)
            return DataGridEditFormAction.Close;

        // If the user has updated the password we need to hash it
        if (item.Password != smtpAccount.Password)
            item.Password = Argon2.Hash(item.Password);

        // Update values in DB
        dbContext.Entry(smtpAccount).CurrentValues.SetValues(item);

        smtpAccount.AllowedSenders.Clear();

        foreach (var sender in item.AllowedSenders)
        {
            smtpAccount.AllowedSenders.Add(new SMTPAccountAllowedSender
            {
                EmailAddress = sender.EmailAddress
            });
        }

        smtpAccount.AllowedRecipients.Clear();

        foreach (var recipient in item.AllowedRecipients)
        {
            smtpAccount.AllowedRecipients.Add(new SMTPAccountAllowedRecipient
            {
                EmailAddress = recipient.EmailAddress
            });
        }

        _ = await dbContext.SaveChangesAsync();

        _ = Snackbar.Add($"SMTP Account updated successfully!", Severity.Success);

        return DataGridEditFormAction.Close;
    }

   

  
}
