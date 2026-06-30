using Microsoft.Graph.Models;
using MustMail.App.Services.Maintenance;

namespace MustMail.App.Services.MailProcessing
{
    public class SmtpAccountAuthorization(IDbContextFactory<DatabaseContext> dbFactory)
    {
        public async Task<bool> CheckSenderIsAllowed(string accountName, string senderAddress)
        {
            await using DatabaseContext dbContext = await dbFactory.CreateDbContextAsync();

            SMTPAccount account = dbContext.SMTPAccount.Include(a => a.AllowedSenders).Single(a => a.Username == accountName);

            // If sender restrictions exist and the sender is not allowed, return false
            if (account.AllowedSenders.Count != 0 && !account.AllowedSenders.Any(allowed =>
              {
                  // Wildcard domain match (*.example.com)
                  if (allowed.EmailAddress.StartsWith("*@"))
                  {
                      return senderAddress.EndsWith(
                          allowed.EmailAddress[1..],
                          StringComparison.OrdinalIgnoreCase);
                  }

                  // Exact email match
                  return string.Equals(
                      allowed.EmailAddress,
                      senderAddress,
                      StringComparison.OrdinalIgnoreCase);
              }))
            {
                return false;
            }


            return true;
        }

        public async Task<List<Recipient>> CheckRecipientIsAllowed(string accountName, List<Recipient> recipients)
        {
            await using DatabaseContext dbContext = await dbFactory.CreateDbContextAsync();

            SMTPAccount account = dbContext.SMTPAccount.Include(a => a.AllowedRecipients).Single(a => a.Username == accountName);

            List<Recipient> rejected = [];

            foreach (Recipient recipient in recipients)
            {
                // If recipient restrictions exist and the recipient is not allowed, add to rejected
                if (account.AllowedRecipients.Count != 0 && !account.AllowedRecipients.Any(allowed =>
                {
                    // Wildcard domain match (*.example.com)
                    if (allowed.EmailAddress.StartsWith("*@"))
                    {
                        return recipient.EmailAddress!.Address!.EndsWith(
                            allowed.EmailAddress[1..],
                            StringComparison.OrdinalIgnoreCase);
                    }

                    // Exact email match
                    return string.Equals(
                        allowed.EmailAddress,
                        recipient.EmailAddress?.Address,
                        StringComparison.OrdinalIgnoreCase);
                }))
                {
                    rejected.Add(recipient);
                }
            }

            return rejected;
        }
    }
}
