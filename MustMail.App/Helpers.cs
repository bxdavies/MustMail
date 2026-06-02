using System.Text.Json;
using System.Text.Json.Serialization;

namespace MustMail.App;

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
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MustMail__Graph__TenantId")))
            throw new InvalidOperationException(
                                                "The environment variable 'MustMail__Graph__TenantId' must be set.");

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MustMail__Graph__ClientId")))
            throw new InvalidOperationException(
                                                "The environment variable 'MustMail__Graph__ClientId' must be set.");

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MustMail__Graph__ClientSecret")))
            throw new InvalidOperationException(
                                                "The environment variable 'MustMail__Graph__ClientSecret' must be set.");

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MustMail__OpenIdConnect__Authority")))
            throw new InvalidOperationException(
                                                "The environment variable 'MustMail__OpenIdConnect__Authority' must be set.");

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MustMail__OpenIdConnect__ClientId")))
            throw new InvalidOperationException(
                                                "The environment variable 'MustMail__OpenIdConnect__ClientId' must be set.");

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MustMail__OpenIdConnect__ClientSecret")))
            throw new InvalidOperationException(
                                                "The environment variable 'MustMail__OpenIdConnect__ClientSecret' must be set.");
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };
}
public static class JsonWriteDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyProperties = true
    };
}