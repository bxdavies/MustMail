using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Tests;

[TestClass]
public class Blazor
{
    private static WebApplicationFactory<Program> _factory = null!;

    [ClassInitialize]
    public static void AssemblyInitialize(TestContext _)
    {
        _factory = new CustomWebApplicationFactory();
    }

    [ClassCleanup]
    public static void AssemblyCleanup(TestContext _)
    {
        _factory.Dispose();
    }

    public TestContext TestContext { get; set; }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("Blazor")]
    [Description("Verifies that the logged out page does not require authentication")]
    [DataRow("/logged-out")]
    public async Task Blazor_NoAuthentication_IsSuccess(string url)
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(url, TestContext.CancellationToken);

        response.EnsureSuccessStatusCode(); // Status Code 200-299
        Assert.IsNotNull(response.Content.Headers.ContentType);
        Assert.AreEqual("text/html; charset=utf-8",
            response.Content.Headers.ContentType.ToString());
    }

    [TestMethod]
    [TestCategory("Integration")]
    [TestCategory("Blazor")]
    [Description("Verifies that root, admin and profile require authentication and redirect correctly")]
    [DataRow("/")]
    [DataRow("/admin")]
    [DataRow("/profile")]
    public async Task Blazor_Authentication_IsRedirectToCorrectHost(string url)
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://127.0.0.1:5000"),
            AllowAutoRedirect = false
        });

        HttpResponseMessage response = await client.GetAsync(url, TestContext.CancellationToken);

        // Redirect status code
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);

        // Redirect url
        string? redirectHost = response.Headers.Location?.Host;
        Assert.IsNotNull(redirectHost);

        // OpenId Connect Authority
        string? openIdConnectAuthority = Environment.GetEnvironmentVariable("OpenIdConnect__Authority");
        Assert.IsNotNull(openIdConnectAuthority);
        string identityProviderHost = new Uri(openIdConnectAuthority).Host;

        // Confirm redirect url matches identity provider 
        Assert.AreEqual(identityProviderHost, redirectHost);
    }
}