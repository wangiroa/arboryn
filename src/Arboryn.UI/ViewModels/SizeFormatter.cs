using System.Globalization;

namespace Arboryn.UI.ViewModels;

/// <summary>Formatage lisible des tailles de fichiers (unités françaises, base 1024).</summary>
public static class SizeFormatter
{
    private static readonly string[] Units = { "o", "Ko", "Mo", "Go", "To", "Po" };

    public static string Humanize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} {Units[0]}";
        }

        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < Units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return string.Format(CultureInfo.CurrentCulture, "{0:0.#} {1}", size, Units[unit]);
    }
}
