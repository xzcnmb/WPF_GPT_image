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
        if (value is not string base64 || string.IsNullOrWhiteSpace(base64))
        {
            return DependencyProperty.UnsetValue;
        }

        try
        {
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
