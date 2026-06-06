using System.Text.Json;
using Arboryn.Domain.ValueObjects;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// Analyse la sortie JSON de <c>fpcalc -raw -json</c> :
/// <c>{ "duration": 123.45, "fingerprint": [ 12345, -678, ... ] }</c>.
/// Le tableau <c>fingerprint</c> contient les sous-empreintes 32 bits signées ; on les
/// réinterprète en <see cref="uint"/> (le motif de bits importe, pas le signe).
/// Pure et testable, sans dépendance au binaire.
/// </summary>
public static class FpcalcOutputParser
{
    public static AudioFingerprint? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("fingerprint", out var array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var subs = new List<uint>(array.GetArrayLength());
        foreach (var element in array.EnumerateArray())
        {
            if (element.TryGetInt64(out var value))
            {
                subs.Add(unchecked((uint)value));
            }
        }

        return subs.Count == 0 ? null : new AudioFingerprint(subs);
    }
}
