using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Gpt2Image.Wpf.Converters;

public sealed class Base64ImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string source || string.IsNullOrWhiteSpace(source))
        {
            return DependencyProperty.UnsetValue;
        }

        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                var remoteImage = new BitmapImage();
                remoteImage.BeginInit();
                remoteImage.CacheOption = BitmapCacheOption.OnDemand;
                remoteImage.UriSource = uri;
                remoteImage.EndInit();
                return remoteImage;
            }

            if (File.Exists(source))
            {
                var localImage = new BitmapImage();
                localImage.BeginInit();
                localImage.CacheOption = BitmapCacheOption.OnLoad;
                localImage.UriSource = new Uri(source, UriKind.Absolute);
                localImage.EndInit();
                localImage.Freeze();
                return localImage;
            }

            var base64 = source;
            var commaIndex = base64.IndexOf(',');
            if (commaIndex >= 0)
            {
                base64 = base64[(commaIndex + 1)..];
            }

            var bytes = System.Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
