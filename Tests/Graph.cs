using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using MimeKit;

namespace Tests;

[TestClass]
public class Graph
{
    private static WebApplicationFactory<Program> _factory = null!;

    [ClassInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _factory = new CustomWebApplicationFactory();
        _factory.CreateClient();
    }

    [ClassCleanup]
    public static void AssemblyCleanup(TestContext _)
    {
        _factory.Dispose();
    }

    public TestContext TestContext { get; set; }

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
    [TestCategory("Graph")]
    [Description("Verifies that Graph gracefully handles a user which does not exist in the tenant")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task Graph_UserDoesNotExistInTenant_IsRejected(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = CreateMessage(subject: "Test: UserDoesNotExistInTenant_IsRejected",
            body: "UserDoesNotExistInTenant_IsRejected", from: "doesnotexist@example.com");

        await Assert.ThrowsAsync<SmtpCommandException>(() => client.SendAsync(message, TestContext.CancellationToken));
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("Graph")]
    [Description("Ensures that Graph can send as a user")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task Graph_User_IsAccepted(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = CreateMessage(subject: "Test: User_IsAccepted", body: "User_IsAccepted",
            from: Test.Config.GraphUser);

        await client.SendAsync(message, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("Graph")]
    [Description("Ensures that Graph can send as a shared mailbox")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task Graph_SharedMailbox_IsAccepted(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = CreateMessage(subject: "Test: SharedMailbox_IsAccepted", body: "SharedMailbox_IsAccepted",
            from: Test.Config.SharedMailbox);

        await client.SendAsync(message, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("Graph")]
    [Description("Ensures that Graph can send as a alias")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task Graph_Alias_IsAccepted(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = CreateMessage(subject: "Test: Alias_IsAccepted", body: "Alias_IsAccepted",
            from: Test.Config.Alias);

        await client.SendAsync(message, TestContext.CancellationToken);
    }
}