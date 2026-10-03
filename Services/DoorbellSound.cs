using System.IO;
using System.Windows;
using System.Windows.Media;
using IdleDash.Core;

namespace IdleDash.Services;

/// <summary>Het geluid als de deurbel gaat: de ingebouwde dingdong, je eigen geluid, of niets.</summary>
public static class DoorbellSound
{
    private static MediaPlayer? _player;

    public static string SoundFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleDash", "sounds");

    public static void Play(DoorbellSettings settings)
    {
        string? file = settings.Sound switch
        {
            "none" => null,
            "custom" when File.Exists(settings.SoundFile) => settings.SoundFile,
            _ => BuiltInFile(),
        };
        if (file == null) return;
        try
        {
            _player?.Close();
            var player = new MediaPlayer { Volume = 1.0 };
            player.MediaEnded += (_, _) => player.Close();
            player.MediaFailed += (_, _) => player.Close();
            player.Open(new Uri(file));
            player.Play();
            _player = player;   // vasthouden, anders stopt het geluid halverwege
        }
        catch
        {
            // geluid afspelen lukt niet: de melding komt er toch
        }
    }

    /// <summary>
    /// Een eigen geluid kopiëren naar de map van IdleDash. Zo blijft het werken als je het origineel verplaatst,
    /// en geven tekens als # in de naam geen problemen.
    /// </summary>
    public static string? ImportCustom(string source)
    {
        try
        {
            Directory.CreateDirectory(SoundFolder);
            string target = Path.Combine(SoundFolder, "eigen-geluid" + Path.GetExtension(source).ToLowerInvariant());
            File.Copy(source, target, overwrite: true);
            return target;
        }
        catch
        {
            return null;
        }
    }

    private static string? BuiltInFile()
    {
        try
        {
            string path = Path.Combine(SoundFolder, "dingdong.wav");
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(SoundFolder);
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/dingdong.wav"));
            if (resource == null) return null;
            using (var source = resource.Stream)
            using (var target = File.Create(path))
                source.CopyTo(target);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
