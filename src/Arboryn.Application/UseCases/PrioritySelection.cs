namespace Arboryn.Application.UseCases;

/// <summary>
/// Logique pure d'aide à la sélection par « répertoire prioritaire » : repère les
/// répertoires récurrents entre groupes de doublons et détermine, pour un groupe,
/// quelle copie conserver. Sans état ni I/O, donc testable directement.
/// </summary>
public static class PrioritySelection
{
    /// <summary>
    /// Classe les répertoires présents dans au moins deux groupes de doublons,
    /// par nombre de groupes décroissant. <paramref name="groupDirectorySets"/> :
    /// pour chaque groupe, l'ensemble (distinct) des répertoires de ses membres.
    /// </summary>
    public static IReadOnlyList<string> RankDirectories(
        IReadOnlyList<IReadOnlyCollection<string>> groupDirectorySets)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var set in groupDirectorySets)
        {
            foreach (var directory in set.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                counts[directory] = counts.GetValueOrDefault(directory) + 1;
            }
        }

        return counts
            .Where(kv => kv.Value >= 2)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .ToList();
    }

    /// <summary>
    /// Index de la copie à conserver dans un groupe. Critères, par ordre de priorité :
    /// <list type="number">
    ///   <item>version la plus haute (suffixe <c>v2</c> / <c>_v3</c> / <c>V4</c>…) — prime sur tout ;</item>
    ///   <item>répertoire prioritaire le mieux classé ;</item>
    ///   <item>si <paramref name="useScore"/>, meilleur <see cref="PreferableScore"/> (profondeur, taille, nom) ;</item>
    ///   <item>première copie.</item>
    /// </list>
    /// </summary>
    public static int ChooseKeepIndex(
        IReadOnlyList<KeepCandidate> members,
        IReadOnlyList<string> orderedPriorityPrefixes,
        bool useScore)
    {
        var best = 0;
        var bestVersion = ExtractVersion(members[0].Name);
        var bestRank = PriorityRank(members[0].Directory, orderedPriorityPrefixes);
        var bestScore = PreferableScore(members[0]);

        for (var i = 1; i < members.Count; i++)
        {
            var version = ExtractVersion(members[i].Name);
            var rank = PriorityRank(members[i].Directory, orderedPriorityPrefixes);
            var score = PreferableScore(members[i]);

            if (IsBetter(version, rank, score, bestVersion, bestRank, bestScore, useScore))
            {
                best = i;
                bestVersion = version;
                bestRank = rank;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool IsBetter(
        int version, int rank, double score,
        int bestVersion, int bestRank, double bestScore,
        bool useScore)
    {
        if (version != bestVersion)
        {
            return version > bestVersion;
        }

        if (rank != bestRank)
        {
            return rank < bestRank;
        }

        return useScore && score > bestScore;
    }

    /// <summary>
    /// Numéro de version déduit du nom de fichier (0 si absent). Reconnaît un marqueur
    /// <c>v</c>/<c>V</c> suivi de chiffres, en début de nom, après un séparateur
    /// (<c>_</c>, <c>-</c>, espace, point) ou en CamelCase (<c>docV3</c>). En cas de
    /// marqueurs multiples, retient le plus élevé.
    /// </summary>
    public static int ExtractVersion(string fileName)
    {
        var stem = StripExtension(fileName);
        var matches = System.Text.RegularExpressions.Regex.Matches(
            stem, @"(?:^|[\s_\-.])[vV](\d+)|(?<=[a-z])V(\d+)");

        var version = 0;
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var digits = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (int.TryParse(digits, out var n) && n > version)
            {
                version = n;
            }
        }

        return version;
    }

    private static string StripExtension(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>
    /// Score « préférable » d'une copie (plus haut = à conserver) : profondeur
    /// d'arborescence dominante (mieux rangée), bonus de taille, pénalité sur les
    /// marqueurs de copie/version dans le nom.
    /// </summary>
    public static double PreferableScore(KeepCandidate member)
    {
        var depth = Depth(member.Directory);
        var sizeBoost = Math.Log2(member.Size + 1);
        var junk = JunkMarkerCount(member.Name);

        return (depth * 1000.0) + sizeBoost - (junk * 500.0);
    }

    private static int JunkMarkerCount(string name)
    {
        var lower = name.ToLowerInvariant();
        var count = 0;

        if (lower.Contains("copy") || lower.Contains("copie"))
        {
            count++;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\(\d+\)"))
        {
            count++;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"[_\- ]v\d+"))
        {
            count++;
        }

        return count;
    }

    /// <summary>Rang du premier préfixe sous lequel se trouve le répertoire, sinon int.MaxValue.</summary>
    private static int PriorityRank(string directory, IReadOnlyList<string> orderedPrefixes)
    {
        for (var i = 0; i < orderedPrefixes.Count; i++)
        {
            if (IsUnder(directory, orderedPrefixes[i]))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>Vrai si <paramref name="directory"/> est égal à, ou situé sous, <paramref name="prefix"/>.</summary>
    private static bool IsUnder(string directory, string prefix)
    {
        if (string.Equals(directory, prefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var withSeparator = prefix.EndsWith('\\') ? prefix : prefix + "\\";
        return directory.StartsWith(withSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Profondeur d'arborescence : nombre de séparateurs dans le chemin.</summary>
    private static int Depth(string directory) => directory.Count(c => c == '\\');
}

/// <summary>Copie candidate à la conservation : répertoire, taille, nom de fichier.</summary>
public readonly record struct KeepCandidate(string Directory, long Size, string Name);
