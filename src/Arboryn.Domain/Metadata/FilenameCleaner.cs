using System.Text.RegularExpressions;

namespace Arboryn.Domain.Metadata;

/// <summary>
/// Extrait du nom de fichier le « titre nettoyé » et les tags techniques (résolution,
/// source, codec, audio, langue, année, groupe de release). Pure et déterministe,
/// pensée pour les conventions de nommage des médias (films, séries, audio, livres).
/// </summary>
public static class FilenameCleaner
{
    public static FilenameMetadata Extract(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return new FilenameMetadata(string.Empty);
        }

        // 1. Retire l'extension, normalise les séparateurs (._- → espace).
        var stem = StripExtension(filename);
        var working = NormalizeSeparators(stem);

        // 2. Extrait et retire chaque tag connu. Ordre : du plus spécifique au plus général.
        var year = MatchAndRemoveYear(ref working);
        var resolution = MatchAndRemove(ref working, ResolutionRegex);
        var source = MatchAndRemove(ref working, SourceRegex);
        var codec = MatchAndRemove(ref working, CodecRegex);
        var audio = MatchAndRemove(ref working, AudioRegex);
        var language = MatchAndRemove(ref working, LanguageRegex);
        // Tags HDR/SDR/10bit : retirés mais non exposés (rare et redondant avec la résolution).
        _ = MatchAndRemove(ref working, HdrRegex);

        // 3. Groupe de release : motif "-XXX" en fin de chaîne.
        var releaseGroup = MatchAndRemoveReleaseGroup(ref working);

        // 4. Nettoie les espaces et la ponctuation résiduelle ("- ", " -").
        var cleanTitle = TrimResidualSeparators(NormalizeWhitespace(working));

        return new FilenameMetadata(cleanTitle, year, resolution, source, codec, audio, language, releaseGroup);
    }

    private static string StripExtension(string filename)
    {
        var dot = filename.LastIndexOf('.');
        return dot > 0 ? filename[..dot] : filename;
    }

    private static string NormalizeSeparators(string value)
        => value.Replace('_', ' ').Replace('.', ' ').Trim();

    private static int? MatchAndRemoveYear(ref string s)
    {
        var match = YearRegex.Match(s);
        if (!match.Success)
        {
            return null;
        }

        if (!int.TryParse(match.Groups[1].Value, out var year))
        {
            return null;
        }

        s = YearRegex.Replace(s, " ");
        return year;
    }

    private static string? MatchAndRemove(ref string s, Regex regex)
    {
        var match = regex.Match(s);
        if (!match.Success)
        {
            return null;
        }

        var captured = match.Groups[1].Success ? match.Groups[1].Value : match.Value;
        // Replace ALL occurrences (ex. « 2160p » et « UHD » dans le même nom).
        s = regex.Replace(s, " ");
        return captured.Trim();
    }

    private static string? MatchAndRemoveReleaseGroup(ref string s)
    {
        var match = ReleaseGroupRegex.Match(s);
        if (!match.Success)
        {
            return null;
        }

        s = s[..match.Index];
        return match.Groups[1].Value;
    }

    private static string NormalizeWhitespace(string value)
        => Regex.Replace(value.Trim(), @"\s+", " ");

    private static string TrimResidualSeparators(string value)
        => Regex.Replace(value, @"\s*-\s*$|^\s*-\s*", string.Empty).Trim();

    // Année plausible (19xx ou 20xx), entourée éventuellement de parenthèses ou crochets.
    private static readonly Regex YearRegex = new(
        @"(?:\(|\[|\s)\s*(19\d{2}|20\d{2})\s*(?:\)|\]|\s|$)",
        RegexOptions.Compiled);

    private static readonly Regex ResolutionRegex = new(
        @"\b(2160p|1080p|720p|540p|480p|4k|uhd)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SourceRegex = new(
        @"\b(bluray|blu-ray|bdrip|brrip|webrip|web-?dl|hdtv|dvdrip|dvd|hdcam|remux|cam|ts)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CodecRegex = new(
        @"\b(x265|x264|h\.?265|h\.?264|hevc|xvid|avc|divx|av1)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AudioRegex = new(
        @"\b(dts-?hd|dts|ac3|aac|flac|opus|dd5\.?1|atmos|truehd)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LanguageRegex = new(
        @"\b(vostfr|truefrench|french|english|multi|fre|eng|vff|vf|vo|spanish|german|italian|dual)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HdrRegex = new(
        @"\b(hdr10\+|hdr10|hdr|sdr|10bit|dolby\s*vision)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Groupe de release : "-NAME" en fin de chaîne, NAME alphanumérique sans espace.
    private static readonly Regex ReleaseGroupRegex = new(
        @"-\s*([A-Za-z][A-Za-z0-9.]*)\s*$",
        RegexOptions.Compiled);
}
