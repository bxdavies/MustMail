using System.Text.Json;

namespace MustMail;

public class Helpers
{
    public static string SanitizeFilePath(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        string fileName = Path.GetFileName(path);

        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '-');
        }

        return Path.Combine(directory, fileName);
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };
}
