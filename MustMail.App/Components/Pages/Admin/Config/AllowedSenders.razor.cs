namespace MustMail.App.Components.Pages.Admin.Config;

public class AllowedSendersBase : ConfigPageBase
{
    protected string NewAllowedSender = "";

    protected void AddAllowedSender()
    {
        string v = NewAllowedSender.Trim();
        if (string.IsNullOrWhiteSpace(v)) return;

        if (!AllowedEmailRegex().IsMatch(v))
        {
            _ = Snackbar.Add(
                "Enter a valid email address or wildcard domain (e.g. user@example.com or *@example.com).",
                Severity.Error);
            return;
        }

        Configuration.Mail.AllowedSenders.Add(v);
        NewAllowedSender = "";
    }

    protected void RemoveAllowedSender(string value)
    {
        _ = Configuration.Mail.AllowedSenders.Remove(value);
    }
}
