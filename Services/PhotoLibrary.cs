using System.IO;

namespace IdleDash.Services;

/// <summary>Foto's in mappen zoeken (voor de foto-widget).</summary>
public static class PhotoLibrary
{
    public static readonly string[] Extensions =
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".heic", ".heif", ".tif", ".tiff", ".jxr" };

    public static bool IsHeic(string path) =>
        path.EndsWith(".heic", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".heif", StringComparison.OrdinalIgnoreCase);

    /// <summary>Alle foto's in de mappen, in de gekozen volgorde (random, name of date).</summary>
    public static List<string> Scan(IEnumerable<string> folders, bool subfolders, string order, int max = 5000)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = subfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        var files = new List<string>();
        foreach (string folder in folders.Where(Directory.Exists))
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*", options))
            {
                if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) files.Add(file);
                if (files.Count >= max) break;
            }
        }

        files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        switch (order)
        {
            case "name":
                files.Sort(StringComparer.CurrentCultureIgnoreCase);
                break;
            case "date":
                files = files.OrderBy(f => File.GetLastWriteTime(f)).ToList();
                break;
            default:
                Shuffle(files);
                break;
        }
        return files;
    }

    private static void Shuffle(List<string> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
