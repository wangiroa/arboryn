namespace Arboryn.Domain.Matching;

/// <summary>
/// Similarité floue entre deux noms (déjà normalisés : minuscules, sans accents,
/// séparateurs en espaces — cf. CanonicalName). Combine une distance d'édition
/// (Levenshtein normalisé) et un recouvrement de tokens. Score dans [0, 1].
///
/// Orientée rappel : sert à proposer des candidats, confirmés ensuite par hash.
/// Rejet ciblé :
///   • deux noms qui ne diffèrent QUE par des « tokens de partition » — marqueurs
///     de chapitre/part/tome/volume/épisode + leurs chiffres, ou chiffres autonomes
///     comme dans les séquences photo (IMG_45, DSC_1234), les dates ou les numéros
///     d'épisode — ne sont PAS des doublons (parties / clichés distincts d'une série) ;
///   • un nom dont le résidu (après retrait de tous les tokens de partition) est
///     trop court pour porter une identité propre — par ex. <c>Chapitre 16.mp3</c>,
///     <c>16 - Chapitre 16.mp3</c>, <c>Track 03.mp3</c>, <c>01.mp3</c> — ne peut
///     pas servir d'identifiant d'œuvre : c'est le dossier parent qui désigne l'œuvre,
///     pas le nom de fichier. La similarité retourne 0 quel que soit l'autre nom.
/// </summary>
public static class FuzzyName
{
    /// <summary>
    /// Nombre minimal de caractères alphanumériques restant dans le résidu (après
    /// retrait des marqueurs de partition) pour qu'un nom porte une identité propre.
    /// En deçà, le fichier n'est désigné que par sa position (chapitre N, track N…),
    /// et le dossier parent est seul à identifier l'œuvre — le match fuzzy n'a pas
    /// de sens (déclenche les faux positifs des séries de chapitres audiobook).
    /// </summary>
    private const int MinResidualAlphanumeric = 3;

    public static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0.0;
        }

        if (HasInsufficientResidual(left) || HasInsufficientResidual(right))
        {
            return 0.0;
        }

        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return 1.0;
        }

        if (DifferOnlyByPartitionTokens(left, right))
        {
            return 0.0;
        }

        var edit = 1.0 - (double)Levenshtein(left, right) / Math.Max(left.Length, right.Length);
        var token = TokenScore(Tokenize(left), Tokenize(right));

        return Math.Max(edit, token);
    }

    /// <summary>
    /// Vrai si, une fois retirés les marqueurs de partition et les chiffres autonomes,
    /// il reste moins de <see cref="MinResidualAlphanumeric"/> caractères alphanumériques —
    /// le nom ne porte alors aucune identité indépendante de sa position dans la série.
    /// </summary>
    private static bool HasInsufficientResidual(string name)
    {
        var stripped = StripPartitionTokens(name);
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

    // Marqueurs de partition : keyword précédé du début ou d'un séparateur
    // (lookbehind non consommé), suivi de zéro+ séparateurs puis de chiffres.
    // Couvre livres (chapitre/tome/partie), vidéo (episode/saison/season) et
    // audio (track/disc/cd) — toutes formes courantes en FR et EN.
    private static readonly System.Text.RegularExpressions.Regex ChapterMarkerRegex = new(
        @"(?i)(?:^|(?<=[\s_\-.]))(chapitre|chapter|episode|saison|season|partie|volume|track|disc|part|tome|vol|cd|ep|ch|pt|tr)[\s_\-.]*(\d+)",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    // Chiffres autonomes courts (1 à 5 chiffres) : couvre les séquences photo (IMG_45,
    // DSC_1234), les numéros à 3 chiffres et les années à 4 chiffres (rapport 2020).
    // Les nombres ≥ 6 chiffres ressemblent à des dates/timestamps (20230501, 120000) et
    // sont conservés : deux fichiers de travail intermédiaires datés peuvent être groupés.
    // N'attrape PAS les chiffres collés à des lettres (« v2 »), ce qui préserve les versions.
    private static readonly System.Text.RegularExpressions.Regex StandaloneNumberRegex = new(
        @"(?:^|(?<=[\s_\-.]))\d{1,5}(?=$|[\s_\-.])",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Vrai si les deux noms, une fois retirés tous leurs marqueurs de chapitre/part et
    /// leurs chiffres autonomes, sont identiques. Cas types :
    /// « hamlet chapitre 1 » vs « hamlet chapitre 2 » et « img 45 » vs « img 46 ».
    /// Les versions <c>vX</c> ne sont PAS retirées (les chiffres y sont collés à une lettre).
    /// </summary>
    private static bool DifferOnlyByPartitionTokens(string left, string right)
    {
        var strippedLeft = StripPartitionTokens(left);
        var strippedRight = StripPartitionTokens(right);
        return string.Equals(strippedLeft, strippedRight, StringComparison.Ordinal);
    }

    private static string StripPartitionTokens(string name)
    {
        var stripped = ChapterMarkerRegex.Replace(name, " ");
        stripped = StandaloneNumberRegex.Replace(stripped, " ");
        return NormalizeWhitespace(stripped);
    }

    private static string NormalizeWhitespace(string value)
        => System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"\s+", " ");

    private static HashSet<string> Tokenize(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Mélange Jaccard (|∩|/|∪|) et coefficient de recouvrement (|∩|/min) — ce dernier
    /// pèse plus pour bien capter les cas d'inclusion (« hamlet » ⊂ « hamlet v2 »).
    /// </summary>
    private static double TokenScore(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0.0;
        }

        var intersection = a.Count(b.Contains);
        if (intersection == 0)
        {
            return 0.0;
        }

        var union = a.Count + b.Count - intersection;
        var jaccard = (double)intersection / union;
        var overlap = (double)intersection / Math.Min(a.Count, b.Count);

        return (0.25 * jaccard) + (0.75 * overlap);
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
