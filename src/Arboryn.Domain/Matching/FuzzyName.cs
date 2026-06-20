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
    public static double Similarity(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0.0;
        }

        if (PartitionMarkers.IsPositionOnly(left) || PartitionMarkers.IsPositionOnly(right))
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
    /// Vrai si les deux noms, une fois retirés tous leurs marqueurs de chapitre/part et
    /// leurs chiffres autonomes, sont identiques. Cas types :
    /// « hamlet chapitre 1 » vs « hamlet chapitre 2 » et « img 45 » vs « img 46 ».
    /// Les versions <c>vX</c> ne sont PAS retirées (les chiffres y sont collés à une lettre).
    /// </summary>
    private static bool DifferOnlyByPartitionTokens(string left, string right)
    {
        var strippedLeft = PartitionMarkers.Strip(left);
        var strippedRight = PartitionMarkers.Strip(right);
        return string.Equals(strippedLeft, strippedRight, StringComparison.Ordinal);
    }

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
