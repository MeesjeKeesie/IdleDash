using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace IdleDash.Core;

/// <summary>Foto's inladen: verkleind tot wat nodig is en rechtop gedraaid (telefoonfoto's staan vaak "gedraaid" opgeslagen).</summary>
public static class PhotoLoader
{
    /// <summary>Foto met de langste kant hooguit maxSize pixels.</summary>
    public static BitmapSource? Load(string path, int maxSize) =>
        Load(path, (w, h) => maxSize <= 0 ? 1 : Math.Min(1, maxSize / (double)Math.Max(w, h)));

    /// <summary>Foto precies zo groot als nodig om het scherm te vullen (fill/stretch) of erin te passen (fit); "center" = origineel.</summary>
    public static BitmapSource? LoadForScreen(string path, double screenWidth, double screenHeight, string fit) =>
        Load(path, (w, h) => fit switch
        {
            "center" => 1,
            "fit" => Math.Min(1, Math.Min(screenWidth / w, screenHeight / h)),
            _ => Math.Min(1, Math.Max(screenWidth / w, screenHeight / h)),
        });

    /// <summary>Alleen het zelf gekozen deel van de foto, zo groot als het scherm nodig heeft.</summary>
    public static BitmapSource? LoadCropped(string path, double screenWidth, double screenHeight, CropRect crop)
    {
        var image = Load(path, (w, h) => Math.Min(1, Math.Max(screenWidth / (crop.W * w), screenHeight / (crop.H * h))));
        if (image == null) return null;
        int pw = image.PixelWidth, ph = image.PixelHeight;
        int x = Math.Clamp((int)Math.Round(crop.X * pw), 0, pw - 1);
        int y = Math.Clamp((int)Math.Round(crop.Y * ph), 0, ph - 1);
        int width = Math.Clamp((int)Math.Round(crop.W * pw), 1, pw - x);
        int height = Math.Clamp((int)Math.Round(crop.H * ph), 1, ph - y);
        var cropped = new CroppedBitmap(image, new Int32Rect(x, y, width, height));
        cropped.Freeze();
        return cropped;
    }

    /// <summary>scaleFor krijgt de breedte en hoogte zoals de foto rechtop staat, en geeft de verkleining (1 = origineel).</summary>
    private static BitmapSource? Load(string path, Func<int, int, double> scaleFor)
    {
        try
        {
            ushort orientation = 1;
            int width, height;
            using (var stream = File.OpenRead(path))
            {
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                width = frame.PixelWidth;
                height = frame.PixelHeight;
                try
                {
                    if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("System.Photo.Orientation")
                        && meta.GetQuery("System.Photo.Orientation") is ushort o)
                        orientation = o;
                }
                catch
                {
                    // geen draai-informatie
                }
            }
            bool sideways = orientation is 5 or 6 or 7 or 8;
            double scale = sideways ? scaleFor(height, width) : scaleFor(width, height);

            using var file = File.OpenRead(path);   // via een stream: werkt ook met # of % in de naam
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = file;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (scale < 1) image.DecodePixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
            image.EndInit();
            image.Freeze();

            double angle = orientation switch { 3 or 4 => 180, 5 or 6 => 90, 7 or 8 => 270, _ => 0 };
            if (angle == 0) return image;
            var rotated = new TransformedBitmap(image, new RotateTransform(angle));
            rotated.Freeze();
            return rotated;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Eén foto op de achtergrond, met eventueel een wazige versie erachter. Twee lagen geven een zachte overgang.</summary>
public sealed class PhotoLayer : Grid
{
    public readonly Image Back = new() { Stretch = Stretch.UniformToFill, Opacity = 0.8, IsHitTestVisible = false };
    public readonly Image Front = new() { IsHitTestVisible = false };

    public PhotoLayer(double blurRadius)
    {
        Back.Effect = new BlurEffect { Radius = blurRadius };
        Back.CacheMode = new BitmapCache();   // de wazige versie maar één keer uitrekenen
        Children.Add(Back);
        Children.Add(Front);
        ClipToBounds = true;
        IsHitTestVisible = false;
        Opacity = 0;
    }

    public bool HasImage => Front.Source != null;

    /// <summary>fit: fill (opvullen), fit (hele foto, wazige rand), stretch (uitrekken), center (centreren). align geldt bij opvullen.</summary>
    public void Show(ImageSource image, string fit, string align)
    {
        Front.Source = image;
        Back.Source = fit == "fit" ? image : null;
        Back.Visibility = fit == "fit" ? Visibility.Visible : Visibility.Collapsed;
        Front.Stretch = fit switch { "fit" => Stretch.Uniform, "stretch" => Stretch.Fill, "center" => Stretch.None, _ => Stretch.UniformToFill };
        if (fit == "center" && image is BitmapSource bitmap)
        {
            // Ware grootte: één pixel van de foto is één pixel op het scherm, ook bij een schaal van 125% of 150%
            var dpi = VisualTreeHelper.GetDpi(this);
            Front.Stretch = Stretch.Fill;
            Front.Width = bitmap.PixelWidth / dpi.DpiScaleX;
            Front.Height = bitmap.PixelHeight / dpi.DpiScaleY;
        }
        else
        {
            Front.Width = double.NaN;
            Front.Height = double.NaN;
        }
        bool fill = fit is not ("fit" or "stretch" or "center");
        Front.HorizontalAlignment = fill && align == "left" ? HorizontalAlignment.Left : fill && align == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        Front.VerticalAlignment = fill && align == "top" ? VerticalAlignment.Top : fill && align == "bottom" ? VerticalAlignment.Bottom : VerticalAlignment.Center;
    }

    public void Clear()
    {
        Front.Source = null;
        Back.Source = null;
    }
}
