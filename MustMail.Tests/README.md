# Running the Tests

The `MustMail.Tests` project contains integration tests that validate the full mail delivery pipeline. These tests require access to external services including Microsoft Graph and an OpenID Connect identity provider.

## Prerequisites

Before running the tests, ensure you have:

- .NET SDK installed
- A configured Microsoft Entra ID application with Microsoft Graph permissions
- An OpenID Connect provider (such as Keycloak)
- A test SMTP account
- Test mailboxes that can send and receive email

## Running the Tests

From the repository root:

Alternatively, from the repository root without changing directory:

```bash
dotnet test MustMail.Tests --settings MustMail.Tests/MustMail.Tests.runsettings --logger "console;verbosity=detailed"
```

## Required Environment Variables

The tests are configured entirely through environment variables.

### Application Configuration

| Variable | Description |
|----------|-------------|
| `MustMail__Serilog_MinimumLevel_Default` | Logging level (typically `Debug` when testing). |
| `MustMail__Certificate__Password` | Password for the test certificate. |

### Microsoft Graph

| Variable | Description |
|----------|-------------|
| `MustMail__Graph__TenantId` | Microsoft Entra tenant ID. |
| `MustMail__Graph__ClientId` | Microsoft Entra application (client) ID. |
| `MustMail__Graph__ClientSecret` | Client secret for the application. |

### OpenID Connect

| Variable | Description |
|----------|-------------|
| `MustMail__OpenIdConnect__Authority` | OIDC authority URL (for example, Keycloak). |
| `MustMail__OpenIdConnect__ClientId` | OIDC client ID. |
| `MustMail__OpenIdConnect__ClientSecret` | OIDC client secret. |

### Bootstrap Configuration

| Variable | Description |
|----------|-------------|
| `MustMail__Bootstrap__SMTPAccounts` | Initial SMTP account(s) to create for the tests. |

### Notification Addresses

| Variable | Description |
|----------|-------------|
| `MustMail__Mail__NotificationSenderAddress` | Sender address used for notification emails. |
| `MustMail__Mail__NotificationRecipientAddress` | Recipient for notification emails generated during testing. |

### SMTP Test Account

| Variable | Description |
|----------|-------------|
| `Test__Smtp__User` | SMTP username. |
| `Test__Smtp__Password` | SMTP password. |
| `Test__Smtp__Sender__Default` | Default sender email address. |
| `Test__Smtp__Recipient__Default` | Primary recipient used by the tests. |
| `Test__Smtp__Recipient__Second` | Secondary recipient used by tests involving multiple recipients. |

### Microsoft Graph Test Accounts

| Variable | Description |
|----------|-------------|
| `Test__Graph__Sender__User` | Standard user mailbox used for Graph tests. |
| `Test__Graph__Sender__SharedMailbox` | Shared mailbox used for Graph tests. |
| `Test__Graph__Sender__Alias` | Alias address used for alias-related tests. |

### Mail Validation

| Variable | Description |
|----------|-------------|
| `Test__Mail__AllowedDomain` | Allowed recipient pattern used during testing (for example `*@example.com`). |

## Example

```text
MustMail__Serilog_MinimumLevel_Default=Debug

MustMail__Graph__TenantId=<TENANT_ID>
MustMail__Graph__ClientId=<CLIENT_ID>
MustMail__Graph__ClientSecret=<CLIENT_SECRET>

MustMail__OpenIdConnect__Authority=https://idp.example.com/realms/master/
MustMail__OpenIdConnect__ClientId=mustmail
MustMail__OpenIdConnect__ClientSecret=<CLIENT_SECRET>

MustMail__Certificate__Password=<PASSWORD>

MustMail__Bootstrap__SMTPAccounts=test:1234

MustMail__Mail__NotificationSenderAddress=servers@example.com
MustMail__Mail__NotificationRecipientAddress=user1@example.com

Test__Smtp__User=test
Test__Smtp__Password=<PASSWORD>
Test__Smtp__Sender__Default=servers@example.com
Test__Smtp__Recipient__Default=user1@example.com
Test__Smtp__Recipient__Second=user2@example.com

Test__Graph__Sender__User=user1@example.com
Test__Graph__Sender__SharedMailbox=servers@example.com
Test__Graph__Sender__Alias=servers@example.com

Test__Mail__AllowedDomain=*@example.com
```

## Notes

- These are integration tests and will send real emails.
- Use dedicated test mailboxes rather than production accounts.
- The Microsoft Graph application must have permission to send mail on behalf of the configured accounts.
- The configured mailboxes must be able to send to the test recipient addresses.
- Running with `Debug` logging is recommended when diagnosing test failures.