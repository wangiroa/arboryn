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
    ///   <item>copie hors d'un répertoire exclu (un répertoire exclu — ex. « Téléchargements » — est
    ///         écarté par défaut quand une autre copie existe) ;</item>
    ///   <item>si <paramref name="useScore"/>, meilleur <see cref="PreferableScore"/> (profondeur, taille, nom) ;</item>
    ///   <item>première copie.</item>
    /// </list>
    /// Les <paramref name="orderedPriorityPrefixes"/> comme les <paramref name="excludedPatterns"/>
    /// acceptent des motifs génériques : un <c>*</c> remplace un segment, un <c>**</c> une profondeur
    /// quelconque, et un suffixe <c>\*</c> désigne tout le sous-arbre (cf. <see cref="MatchesPattern"/>).
    /// </summary>
    public static int ChooseKeepIndex(
        IReadOnlyList<KeepCandidate> members,
        IReadOnlyList<string> orderedPriorityPrefixes,
        bool useScore,
        IReadOnlyList<string>? excludedPatterns = null)
    {
        var best = 0;
        var bestVersion = ExtractVersion(members[0].Name);
        var bestRank = PriorityRank(members[0].Directory, orderedPriorityPrefixes);
        var bestExcluded = IsExcluded(members[0].Directory, excludedPatterns);
        var bestScore = PreferableScore(members[0]);

        for (var i = 1; i < members.Count; i++)
        {
            var version = ExtractVersion(members[i].Name);
            var rank = PriorityRank(members[i].Directory, orderedPriorityPrefixes);
            var excluded = IsExcluded(members[i].Directory, excludedPatterns);
            var score = PreferableScore(members[i]);

            if (IsBetter(version, rank, excluded, score, bestVersion, bestRank, bestExcluded, bestScore, useScore))
            {
                best = i;
                bestVersion = version;
                bestRank = rank;
                bestExcluded = excluded;
                bestScore = score;
            }
        }

        return best;
    }

    private static bool IsBetter(
        int version, int rank, bool excluded, double score,
        int bestVersion, int bestRank, bool bestExcluded, double bestScore,
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

        if (excluded != bestExcluded)
        {
            return !excluded;
        }

        return useScore && score > bestScore;
    }

    /// <summary>Vrai si le répertoire correspond à au moins un motif d'exclusion.</summary>
    private static bool IsExcluded(string directory, IReadOnlyList<string>? excludedPatterns)
    {
        if (excludedPatterns is null)
        {
            return false;
        }

        foreach (var pattern in excludedPatterns)
        {
            if (MatchesPattern(directory, pattern))
            {
                return true;
            }
        }

        return false;
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

    /// <summary>Rang du premier motif auquel correspond le répertoire, sinon int.MaxValue.</summary>
    private static int PriorityRank(string directory, IReadOnlyList<string> orderedPrefixes)
    {
        for (var i = 0; i < orderedPrefixes.Count; i++)
        {
            if (MatchesPattern(directory, orderedPrefixes[i]))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Vrai si <paramref name="directory"/> correspond au motif <paramref name="pattern"/>, ou est
    /// situé sous lui. Le motif est interprété segment par segment :
    /// <list type="bullet">
    ///   <item>un segment littéral correspond au segment de même nom (insensible à la casse) ;</item>
    ///   <item><c>*</c> dans un segment remplace n'importe quelle suite de caractères (ex. <c>Doc*</c>) ;</item>
    ///   <item>un segment <c>**</c> absorbe une profondeur quelconque (ex. <c>C:\Users\**\Downloads</c>) ;</item>
    ///   <item>un suffixe <c>\*</c> ou <c>\**</c> désigne le sous-arbre complet (ex. <c>C:\Livres\*</c>).</item>
    /// </list>
    /// Un motif sans caractère générique se comporte comme un préfixe d'arborescence (le répertoire
    /// lui-même et tout ce qui se trouve dessous), conformément au comportement historique.
    /// </summary>
    public static bool MatchesPattern(string directory, string pattern)
    {
        var pat = NormalizePattern(pattern);
        if (pat.Length == 0)
        {
            return false;
        }

        var dirSegments = NormalizeForMatch(directory).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var patSegments = pat.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return PrefixMatch(dirSegments, 0, patSegments, 0);
    }

    /// <summary>
    /// Vrai si <paramref name="pat"/> (à partir de <paramref name="pi"/>) correspond à un préfixe des
    /// segments de <paramref name="dir"/> (à partir de <paramref name="di"/>) : le répertoire peut être
    /// plus profond que le motif (sémantique de sous-arbre). <c>**</c> absorbe 0..N segments.
    /// </summary>
    private static bool PrefixMatch(string[] dir, int di, string[] pat, int pi)
    {
        while (pi < pat.Length)
        {
            if (pat[pi] == "**")
            {
                for (var skip = 0; di + skip <= dir.Length; skip++)
                {
                    if (PrefixMatch(dir, di + skip, pat, pi + 1))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (di >= dir.Length ||
                !System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pat[pi], dir[di], ignoreCase: true))
            {
                return false;
            }

            di++;
            pi++;
        }

        // Motif entièrement consommé : tout ce qui reste dans le répertoire est en sous-arbre.
        return true;
    }

    /// <summary>Normalise un répertoire pour comparaison : séparateurs <c>\</c>, sans séparateur final.</summary>
    private static string NormalizeForMatch(string directory)
        => directory.Replace('/', '\\').TrimEnd('\\');

    /// <summary>
    /// Normalise un motif : séparateurs <c>\</c>, espaces et séparateur final retirés, et suffixe
    /// de sous-arbre (<c>\*</c> / <c>\**</c>) retiré — un motif consommé désigne déjà le sous-arbre.
    /// </summary>
    private static string NormalizePattern(string pattern)
    {
        var s = pattern.Replace('/', '\\').Trim().TrimEnd('\\');

        if (s.EndsWith(@"\**", StringComparison.Ordinal))
        {
            return s[..^3];
        }

        return s.EndsWith(@"\*", StringComparison.Ordinal) ? s[..^2] : s;
    }

    /// <summary>Profondeur d'arborescence : nombre de séparateurs dans le chemin.</summary>
    private static int Depth(string directory) => directory.Count(c => c == '\\');
}

/// <summary>Copie candidate à la conservation : répertoire, taille, nom de fichier.</summary>
public readonly record struct KeepCandidate(string Directory, long Size, string Name);
