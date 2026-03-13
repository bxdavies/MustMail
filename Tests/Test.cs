using Microsoft.AspNetCore.Mvc.Testing;

namespace Tests;

public static class Test
{
    public static class Config
    {
        public static string SmtpUser =>
            Environment.GetEnvironmentVariable("Test__Smtp__User") ?? "test";

        public static string SmtpPassword =>
            Environment.GetEnvironmentVariable("Test__Smtp__Password") ?? "password";

        public static string DefaultSender =>
            Environment.GetEnvironmentVariable("Test__Smtp__Sender__Default") ?? "sender@example.com";

        public static string DefaultRecipient =>
            Environment.GetEnvironmentVariable("Test__Smtp__Recipient__Default") ?? "recipient@example.com";

        public static string SecondRecipient =>
            Environment.GetEnvironmentVariable("Test__Smtp__Recipient__Second") ?? "recipient@example.com";

        public static string GraphUser =>
            Environment.GetEnvironmentVariable("Test__Graph__Sender__User") ?? "user@example.com";

        public static string SharedMailbox =>
            Environment.GetEnvironmentVariable("Test__Graph__Sender__SharedMailbox") ?? "shared@example.com";

        public static string Alias =>
            Environment.GetEnvironmentVariable("Test__Graph__Sender__Alias") ?? "alias@example.com";
    }
}

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseKestrel();
        builder.UseUrls("http://127.0.0.1:5000");
    }
}