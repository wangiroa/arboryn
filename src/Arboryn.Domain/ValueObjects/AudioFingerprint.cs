using System.Buffers.Binary;
using System.Globalization;

namespace Arboryn.Domain.ValueObjects;

/// <summary>
/// Empreinte acoustique d'un fichier audio (Chromaprint) : suite de sous-empreintes
/// 32 bits, une par trame (~0,124 s). Deux enregistrements du même morceau — même
/// après ré-encodage (MP3, FLAC…) — ont des suites quasi identiques trame à trame.
///
/// Sérialisée en base64 des octets little-endian pour la colonne <c>chromaprint</c>.
/// </summary>
public sealed class AudioFingerprint
{
    private readonly uint[] _subFingerprints;

    public AudioFingerprint(IReadOnlyList<uint> subFingerprints)
    {
        ArgumentNullException.ThrowIfNull(subFingerprints);
        _subFingerprints = subFingerprints.ToArray();
    }

    public IReadOnlyList<uint> SubFingerprints => _subFingerprints;

    /// <summary>Nombre de trames — proportionnel à la durée (~0,124 s par trame).</summary>
    public int Length => _subFingerprints.Length;

    public string Encode()
    {
        var bytes = new byte[_subFingerprints.Length * sizeof(uint)];
        for (var i = 0; i < _subFingerprints.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint)), _subFingerprints[i]);
        }

        return Convert.ToBase64String(bytes);
    }

    public static AudioFingerprint Decode(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            throw new ArgumentException("L'empreinte encodée ne peut pas être vide.", nameof(encoded));
        }

        var bytes = Convert.FromBase64String(encoded);
        var subs = new uint[bytes.Length / sizeof(uint)];
        for (var i = 0; i < subs.Length; i++)
        {
            subs[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint)));
        }

        return new AudioFingerprint(subs);
    }

    /// <summary>
    /// Empreinte courte et stable (FNV-1a 64 bits, 16 hex) du contenu, utilisée comme
    /// signature de LogicalFile pour un groupe acoustique (le représentant du groupe).
    /// </summary>
    public string StableDigest()
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        foreach (var sub in _subFingerprints)
        {
            for (var b = 0; b < sizeof(uint); b++)
            {
                hash ^= (byte)(sub >> (b * 8));
                hash *= prime;
            }
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}
