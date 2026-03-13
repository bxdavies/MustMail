using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using MimeKit;

namespace Tests;

[TestClass]
public class Smtp
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
    [TestCategory("SMTP")]
    [Description("Verifies that we can connect to the SMTP server")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_Connects(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        Assert.IsTrue(client.IsConnected);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Ensures bad username or password can't authenticate")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_BadCredentials_ThrowsAuthenticationException(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            client.AuthenticateAsync("bad", "bad", TestContext.CancellationToken));
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Ensures correct username and password can authenticate")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_ValidCredentials_Authenticates(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        Assert.IsTrue(client.IsAuthenticated);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Ensures we can send emails after authenticating with the correct credentials")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_CanSendMessage_AfterAuthentication(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = CreateMessage(subject: "Test: CanSendMessage_AfterAuthentication",
            body: "CanSendMessage_AfterAuthentication");

        await client.SendAsync(message, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Ensures we can't send emails without authenticating")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_SendWithoutAuthentication_IsRejected(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        MimeMessage message = CreateMessage(subject: "Test: SendWithoutAuthentication_IsRejected",
            body: "SendWithoutAuthentication_IsRejected");

        await Assert.ThrowsAsync<MailKit.ServiceNotAuthenticatedException>(() =>
            client.SendAsync(message, TestContext.CancellationToken));
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Ensures we can't send emails using a bad from address")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_InvalidFromAddress_IsRejected(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = CreateMessage("not-an-email", subject: "Test: InvalidFromAddress_IsRejected",
            body: "InvalidFromAddress_IsRejected");

        await Assert.ThrowsAsync<MailKit.CommandException>(() =>
            client.SendAsync(message, TestContext.CancellationToken));
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Ensures we can't send emails to an address using a bad address")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_InvalidToAddress_IsRejected(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = CreateMessage(subject: "Test: InvalidToAddress_IsRejected",
            body: "InvalidToAddress_IsRejected", to: "not-an-email");

        await Assert.ThrowsAsync<MailKit.CommandException>(() =>
            client.SendAsync(message, TestContext.CancellationToken));
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Ensures we can't cc to an address using a bad address")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_InvalidCcAddress_IsRejected(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = new();
        message.Subject = "Test: InvalidCcAddress_IsRejected";
        message.Body = new TextPart("plain") { Text = "InvalidCcAddress_IsRejected" };
        message.Sender = MailboxAddress.Parse(Test.Config.DefaultSender);
        message.Cc.Add(MailboxAddress.Parse("not-an-email"));

        await Assert.ThrowsAsync<MailKit.CommandException>(() =>
            client.SendAsync(message, TestContext.CancellationToken));
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Ensures we can't bcc to an address using a bad address")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_InvalidBccAddress_IsRejected(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = new();
        message.Subject = "Test: InvalidBccAddress_IsRejected";
        message.Body = new TextPart("plain") { Text = "InvalidBccAddress_IsRejected" };
        message.Sender = MailboxAddress.Parse(Test.Config.DefaultSender);
        message.Bcc.Add(MailboxAddress.Parse("not-an-email"));

        await Assert.ThrowsAsync<MailKit.CommandException>(() =>
            client.SendAsync(message, TestContext.CancellationToken));
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Verifies we can send an email with an empty body")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_EmptyMessageBody_IsAccepted(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = CreateMessage(subject: "Test: EmptyMessageBody_IsAccepted", body: string.Empty);

        await client.SendAsync(message, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Verifies we can send an email to multiple recipients")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_MultipleRecipients_IsAccepted(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = CreateMessage(subject: "Test: MultipleRecipients_IsAccepted",
            body: "MultipleRecipients_IsAccepted");

        message.To.Add(MailboxAddress.Parse(Test.Config.SecondRecipient));

        await client.SendAsync(message, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Verifies we can send an email using cc and bcc")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_CcAndBcc_AreAccepted(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = new();

        message.Subject = "Test: CcAndBcc_AreAccepted";
        message.Body = new TextPart("plain") { Text = "CcAndBcc_AreAccepted" };
        message.Sender = MailboxAddress.Parse(Test.Config.DefaultSender);
        message.To.Add(MailboxAddress.Parse(Test.Config.DefaultSender));
        message.Cc.Add(MailboxAddress.Parse(Test.Config.DefaultRecipient));
        message.Bcc.Add(MailboxAddress.Parse(Test.Config.SecondRecipient));

        await client.SendAsync(message, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Verifies we can send an email with an attachment")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_Attachment_IsAccepted(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = CreateMessage(subject: "Test: Attachment_IsAccepted", body: "Attachment_IsAccepted");

        BodyBuilder builder = new()
        {
            TextBody = "Message with attachment"
        };

        builder.Attachments.Add("test.txt", System.Text.Encoding.UTF8.GetBytes("hello"));

        message.Body = builder.ToMessageBody();

        await client.SendAsync(message, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Verifies we can send an email with an inline attachment")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_InlineAttachment_IsAccepted(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(
            Test.Config.SmtpUser,
            Test.Config.SmtpPassword,
            TestContext.CancellationToken);

        MimeMessage message = CreateMessage(
            subject: "Test: InlineAttachment_IsAccepted",
            body: "InlineAttachment_IsAccepted");

        BodyBuilder builder = new()
        {
            HtmlBody = "<html><body><p>Inline image below:</p><img src=\"cid:test-image\"></body></html>"
        };

        MimePart image = new("image", "png")
        {
            Content = new MimeContent(new MemoryStream([1, 2, 3, 4])),
            ContentId = "test-image",
            ContentDisposition = new ContentDisposition(ContentDisposition.Inline),
            ContentTransferEncoding = ContentEncoding.Base64
        };

        builder.LinkedResources.Add(image);

        message.Body = builder.ToMessageBody();

        await client.SendAsync(message, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Verifies we can send an email with an embedded message")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_EmbeddedMessage_IsAccepted(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(
            Test.Config.SmtpUser,
            Test.Config.SmtpPassword,
            TestContext.CancellationToken);

        MimeMessage message = CreateMessage(
            subject: "Test: EmbeddedMessage_IsAccepted",
            body: "EmbeddedMessage_IsAccepted");

        BodyBuilder builder = new()
        {
            TextBody = "This email contains another email as an attachment"
        };

        MimeMessage embedded = new();
        embedded.From.Add(MailboxAddress.Parse("inner@example.com"));
        embedded.To.Add(MailboxAddress.Parse("recipient@example.com"));
        embedded.Subject = "Inner message";
        embedded.Body = new TextPart("plain")
        {
            Text = "Hello from embedded message"
        };

        // Convert embedded message to bytes
        byte[] embeddedBytes;
        using (MemoryStream ms = new())
        {
            await embedded.WriteToAsync(ms, TestContext.CancellationToken);
            embeddedBytes = ms.ToArray();
        }

        // Attach as message/rfc822
        builder.Attachments.Add(
            "forwarded.eml",
            embeddedBytes,
            ContentType.Parse("message/rfc822"));

        message.Body = builder.ToMessageBody();

        await client.SendAsync(message, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Verifies we can send an email with a HTML body")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_HtmlBody_IsAccepted(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.AuthenticateAsync(Test.Config.SmtpUser, Test.Config.SmtpPassword, TestContext.CancellationToken);

        MimeMessage message = CreateMessage(subject: "Test: HtmlBody_IsAccepted", body: "HtmlBody_IsAccepted");

        BodyBuilder builder = new()
        {
            HtmlBody = "<h1>Hello</h1><p>This is HTML</p>"
        };

        message.Body = builder.ToMessageBody();

        await client.SendAsync(message, TestContext.CancellationToken);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("SMTP")]
    [Description("Verifies the SMTP server disconnected cleanly")]
    [DataRow(465)]
    [DataRow(587)]
    public async Task SMTP_DisconnectsCleanly(int port)
    {
        using SmtpClient client = await ConnectClientAsync(port);

        await client.DisconnectAsync(true, TestContext.CancellationToken);

        Assert.IsFalse(client.IsConnected);
    }
}