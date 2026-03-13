using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Tests;

[TestClass]
public class MustMail
{
    private static MimeMessage CreateMessage(
        string? from = null,
        string? to = null,
        string subject = "Test message",
        string body = "Hello from test")
    {
        MimeMessage message = new();
        message.From.Add(MailboxAddress.Parse(from ?? Test.Config.DefaultSender));
        message.To.Add(MailboxAddress.Parse(to ?? Test.Config.DefaultRecipient));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };
        return message;
    }

    public TestContext TestContext { get; set; }

    private static async Task<SmtpClient> ConnectClientAsync(int port)
    {
        SmtpClient client = new();
        client.ServerCertificateValidationCallback = (_, _, _, _) => true;

        SecureSocketOptions socketOptions = port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.Auto
        };

        await client.ConnectAsync("localhost", port, socketOptions);
        client.Timeout = 10000; // 10 seconds
        return client;
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("MustMail")]
    [Description("Verifies attempting to send from a sender not in the allowed senders list is rejected")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task MustMail_NotAllowedSender_IsRejected(int port)
    {
        Environment.SetEnvironmentVariable("MustMail__AllowedSenders__0", "sender@example.com");

        try
        {
            await using CustomWebApplicationFactory factory = new();

            using HttpClient webClient = factory.CreateClient();

            using SmtpClient client = await ConnectClientAsync(port);

            await client.AuthenticateAsync(
                Test.Config.SmtpUser,
                Test.Config.SmtpPassword,
                TestContext.CancellationToken);

            MimeMessage message = CreateMessage(
                subject: $"Test: NotAllowedSender_IsRejected {port}",
                body: "NotAllowedSender_IsRejected");

            await Assert.ThrowsAsync<SmtpCommandException>(() =>
                client.SendAsync(message, TestContext.CancellationToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MustMail__AllowedSenders__0", null);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("MustMail")]
    [Description("Verifies attempting to send from a sender in the allowed senders list is accepted")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task MustMail_AllowedSender_IsAccepted(int port)
    {
        Environment.SetEnvironmentVariable("MustMail__AllowedSenders__0", Test.Config.DefaultSender);

        try
        {
            await using CustomWebApplicationFactory factory = new();

            using HttpClient webClient = factory.CreateClient();

            using SmtpClient client = await ConnectClientAsync(port);

            await client.AuthenticateAsync(
                Test.Config.SmtpUser,
                Test.Config.SmtpPassword,
                TestContext.CancellationToken);

            MimeMessage message = CreateMessage(
                subject: $"Test: AllowedSender_IsAccepted {port}",
                body: "AllowedSender_IsAccepted");

            await client.SendAsync(message, TestContext.CancellationToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MustMail__AllowedSenders__0", null);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    [Description("Verifies attempting to send to an address not in the allowed recipients list is rejected")]
    [TestCategory("MustMail")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task MustMail_NotAllowedRecipients_IsRejected(int port)
    {
        Environment.SetEnvironmentVariable("MustMail__AllowedRecipients__0", "user@example.com");

        try
        {
            await using CustomWebApplicationFactory factory = new();

            using HttpClient webClient = factory.CreateClient();

            using SmtpClient client = await ConnectClientAsync(port);

            await client.AuthenticateAsync(
                Test.Config.SmtpUser,
                Test.Config.SmtpPassword,
                TestContext.CancellationToken);

            MimeMessage message = CreateMessage(
                subject: $"Test: NotAllowedRecipients_IsRejected {port}",
                body: "NotAllowedRecipients_IsRejected");

            await Assert.ThrowsAsync<SmtpCommandException>(() =>
                client.SendAsync(message, TestContext.CancellationToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MustMail__AllowedRecipients__0", null);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("MustMail")]
    [Description("Verifies attempting to send to an address in the allowed recipients list is accepted")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task MustMail_AllowedRecipients_IsAccepted(int port)
    {
        Environment.SetEnvironmentVariable("MustMail__AllowedRecipients__0", Test.Config.DefaultRecipient);

        try
        {
            await using CustomWebApplicationFactory factory = new();

            using HttpClient webClient = factory.CreateClient();

            using SmtpClient client = await ConnectClientAsync(port);

            await client.AuthenticateAsync(
                Test.Config.SmtpUser,
                Test.Config.SmtpPassword,
                TestContext.CancellationToken);

            MimeMessage message = CreateMessage(
                subject: $"Test: AllowedRecipients_IsAccepted {port}",
                body: "AllowedRecipients_IsAccepted");

            await client.SendAsync(message, TestContext.CancellationToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MustMail__AllowedRecipients__0", null);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("MustMail")]
    [Description(
        "Verifies that if a MAIL FROM address is not found and TrustFrom is disabled then the email is rejected")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task MustMail_TrustFromDisabled_IsRejected(int port)
    {
        Environment.SetEnvironmentVariable("MustMail__TrustFrom", "false");

        try
        {
            await using CustomWebApplicationFactory factory = new();

            using HttpClient webClient = factory.CreateClient();

            using SmtpClient client = await ConnectClientAsync(port);

            await client.AuthenticateAsync(
                Test.Config.SmtpUser,
                Test.Config.SmtpPassword,
                TestContext.CancellationToken);

            MimeMessage message = new();
            message.To.Add(MailboxAddress.Parse(Test.Config.DefaultRecipient));
            message.Subject = $"Test: TrustFromDisabled_IsRejected {port}";
            message.Body = new TextPart("plain") { Text = "TrustFromDisabled_IsRejected" };

            List<MailboxAddress> recipients = [MailboxAddress.Parse(Test.Config.DefaultRecipient)];

            await Assert.ThrowsAsync<SmtpCommandException>(() => client.SendAsync(message,
                new MailboxAddress(string.Empty, string.Empty), recipients, TestContext.CancellationToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MustMail__TrustFrom", null);
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("MustMail")]
    [Description(
        "Verifies that if a MAIL FROM address is not found and TrustFrom is enabled then the FROM address is used and accepted")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task MustMail_TrustFromEnabled_IsAccepted(int port)
    {
        Environment.SetEnvironmentVariable("MustMail__TrustFrom", "true");

        try
        {
            await using CustomWebApplicationFactory factory = new();

            using HttpClient webClient = factory.CreateClient();

            using SmtpClient client = await ConnectClientAsync(port);

            await client.AuthenticateAsync(
                Test.Config.SmtpUser,
                Test.Config.SmtpPassword,
                TestContext.CancellationToken);

            MimeMessage message = new();
            message.From.Add(MailboxAddress.Parse(Test.Config.DefaultSender));
            message.To.Add(MailboxAddress.Parse(Test.Config.DefaultRecipient));
            message.Subject = $"Test: TrustFromEnabled_IsAccepted {port}";
            message.Body = new TextPart("plain") { Text = "TrustFromEnabled_IsAccepted" };

            List<MailboxAddress> recipients = [MailboxAddress.Parse(Test.Config.DefaultRecipient)];

            await client.SendAsync(message, new MailboxAddress(string.Empty, string.Empty), recipients,
                TestContext.CancellationToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MustMail__TrustFrom", null);
        }
    }
}