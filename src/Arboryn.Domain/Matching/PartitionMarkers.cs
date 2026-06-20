using System.Text.RegularExpressions;

namespace Arboryn.Domain.Matching;

/// <summary>
/// Détection des « marqueurs de partition » dans un nom : mots-clés de
/// chapitre/part/tome/volume/épisode suivis de chiffres, et chiffres autonomes courts.
/// Sert à reconnaître les noms qui ne désignent qu'une position dans une série
/// (« Chapitre 16 », « 001 », « Track 03 ») et dont l'identité réelle est portée par le
/// dossier parent, pas par le fichier lui-même.
///
/// Partagé entre la détection de doublons floue (<see cref="FuzzyName"/>) et
/// l'uniformisation (titre d'œuvre tiré du dossier pour les œuvres multi-fichiers comme
/// les livres audio découpés en pistes). Pur et déterministe, sans I/O.
/// </summary>
public static class PartitionMarkers
{
    /// <summary>
    /// Nombre minimal de caractères alphanumériques restant dans le résidu (après retrait
    /// des marqueurs de partition) pour qu'un nom porte une identité propre. En deçà, le
    /// fichier n'est désigné que par sa position et c'est le dossier parent qui identifie
    /// l'œuvre.
    /// </summary>
    public const int MinResidualAlphanumeric = 3;

    // Marqueur de partition : mot-clé (FR + EN) précédé du début ou d'un séparateur
    // (lookbehind non consommé), suivi de zéro+ séparateurs puis de chiffres. Couvre livres
    // (chapitre/tome/partie), vidéo (episode/saison/season) et audio (track/disc/cd).
    private static readonly Regex ChapterMarkerRegex = new(
        @"(?i)(?:^|(?<=[\s_\-.]))(chapitre|chapter|episode|saison|season|partie|volume|track|disc|part|tome|vol|cd|ep|ch|pt|tr)[\s_\-.]*(\d+)",
        RegexOptions.Compiled);

    // Chiffres autonomes courts (1 à 5 chiffres) : couvre les séquences photo (IMG_45,
    // DSC_1234), les numéros à 3 chiffres et les années à 4 chiffres. Les nombres ≥ 6
    // chiffres ressemblent à des dates/timestamps et sont conservés. N'attrape PAS les
    // chiffres collés à des lettres (« v2 »), ce qui préserve les versions.
    private static readonly Regex StandaloneNumberRegex = new(
        @"(?:^|(?<=[\s_\-.]))\d{1,5}(?=$|[\s_\-.])",
        RegexOptions.Compiled);

    // Nombre court ouvrant le nom (capturé) — par ex. « 001 » dans « 001 chapitre 1 »
    // ou « 12 » dans « 12 - … ». Sert à retrouver le numéro de séquence d'un fichier.
    private static readonly Regex LeadingNumberRegex = new(
        @"^[\s_\-.]*(\d{1,5})(?=$|[\s_\-.])",
        RegexOptions.Compiled);

    /// <summary>
    /// Retire tous les marqueurs de partition et chiffres autonomes du nom, et normalise
    /// les espaces. Ce qui reste est le « résidu » identitaire du nom.
    /// </summary>
    public static string Strip(string name)
    {
        var stripped = ChapterMarkerRegex.Replace(name, " ");
        stripped = StandaloneNumberRegex.Replace(stripped, " ");
        return NormalizeWhitespace(stripped);
    }

    /// <summary>
    /// Vrai si, une fois retirés les marqueurs de partition, il reste moins de
    /// <see cref="MinResidualAlphanumeric"/> caractères alphanumériques — le nom ne porte
    /// alors aucune identité indépendante de sa position dans la série.
    /// </summary>
    public static bool IsPositionOnly(string name)
    {
        var stripped = Strip(name);
        var alnum = 0;
        foreach (var c in stripped)
        {
            if (char.IsLetterOrDigit(c))
            {
                alnum++;
                if (alnum >= MinResidualAlphanumeric)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Numéro de séquence porté par le nom : le nombre en tête (« 001 chapitre 1 » → 1)
    /// ou, à défaut, celui du marqueur de partition (« chapitre 7 » → 7). <c>null</c> si
    /// le nom ne porte aucun numéro.
    /// </summary>
    public static int? FirstNumber(string name)
    {
        var leading = LeadingNumberRegex.Match(name);
        if (leading.Success && int.TryParse(leading.Groups[1].Value, out var n))
        {
            return n;
        }

        var marker = ChapterMarkerRegex.Match(name);
        if (marker.Success && int.TryParse(marker.Groups[2].Value, out var m))
        {
            return m;
        }

        return null;
    }

    private static string NormalizeWhitespace(string value)
        => Regex.Replace(value.Trim(), @"\s+", " ");
}
