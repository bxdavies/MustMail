namespace MustMail.App.Components.Pages.Admin;
    public class UsersBase : ComponentBase
    {
    // Page variables
    protected List<User> Users = [];
    protected MudDataGrid<User> UserGrid = null!;

    // Class variables
    private string _searchString = null!;

    // Component parameters and dependency injection
    [Inject] public ISnackbar Snackbar { get; set; } = null!;
    [Inject] public IDbContextFactory<DatabaseContext> DbFactory { get; set; } = null!;

    // Load users - Used by mud data grid to load the data server side using pagnation and supporting search
    protected async Task<GridData<User>> ServerReload(GridState<User> state, CancellationToken token)
    { 
        await using DatabaseContext dbContext = await DbFactory.CreateDbContextAsync(token);
        var query = dbContext.User.AsQueryable();

        if (!string.IsNullOrWhiteSpace(_searchString))
            query = query.Where(u => u.Name.Contains(_searchString)
                                  || u.Email.Contains(_searchString));

        int total = await query.CountAsync(cancellationToken: token);

        List<User> items = await query
            .OrderBy(u => u.Name)
            .Skip(state.Page * state.PageSize)
            .Take(state.PageSize)
            .ToListAsync(cancellationToken: token);

        return new GridData<User> { Items = items, TotalItems = total };
    }

    // On Search - On search string changed reload the server data with the search string
    protected Task OnSearch(string text)
    {
        _searchString = text;
        return UserGrid.ReloadServerData();
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
}

