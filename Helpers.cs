using System.Text.Json;

namespace MustMail;

public static class Helpers
{
    public static string SanitizeFilePath(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        string fileName = Path.GetFileName(path);

        fileName = Path.GetInvalidFileNameChars().Aggregate(fileName, (current, c) => current.Replace(c, '-'));

        return Path.Combine(directory, fileName);
    }

    public static void ValidateEnvironmentVariables()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Graph__TenantId")))
            throw new InvalidOperationException(
                "The environment variable 'Graph__TenantId' must be set.");

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Graph__ClientId")))
            throw new InvalidOperationException(
                "The environment variable 'Graph__ClientId' must be set.");

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Graph__ClientSecret")))
            throw new InvalidOperationException(
                "The environment variable 'Graph__ClientSecret' must be set.");

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OpenIdConnect__Authority")))
            throw new InvalidOperationException(
                "The environment variable 'OpenIdConnect__Authority' must be set.");

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OpenIdConnect__ClientId")))
            throw new InvalidOperationException(
                "The environment variable 'OpenIdConnect__ClientId' must be set.");

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OpenIdConnect__ClientSecret")))
            throw new InvalidOperationException(
                "The environment variable 'OpenIdConnect__ClientSecret' must be set.");

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("Certificate__Password")))
            throw new InvalidOperationException(
                "The environment variable 'Certificate__Password' must be set.");
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };
}
