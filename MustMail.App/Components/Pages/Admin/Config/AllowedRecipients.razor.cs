namespace MustMail.App.Components.Pages.Admin.Config;

public class AllowedRecipientsBase : ConfigPageBase
{
    protected string NewAllowedRecipient = "";

    protected void AddAllowedRecipient()
    {
        string v = NewAllowedRecipient.Trim();
        if (string.IsNullOrWhiteSpace(v)) return;

        if (!AllowedEmailRegex().IsMatch(v))
        {
            _ = Snackbar.Add(
                "Enter a valid email address or wildcard domain (e.g. user@example.com or *@example.com).",
                Severity.Error);
            return;
        }

        Configuration.Mail.AllowedRecipients.Add(v);
        NewAllowedRecipient = "";
    }

    protected void RemoveAllowedRecipient(string value)
    {
        _ = Configuration.Mail.AllowedRecipients.Remove(value);
    }
}
