using Arboryn.Domain.Matching;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class ChromaprintMatcherTests
{
    [Fact]
    public void Similarity_IdenticalFingerprints_IsOne()
    {
        var fp = Fingerprint(seed: 1, length: 200);
        ChromaprintMatcher.Similarity(fp, fp).Should().Be(1.0);
    }

    [Fact]
    public void Similarity_SameTrackReEncoded_IsHigh()
    {
        var original = Fingerprint(seed: 7, length: 300);
        // Ré-encodage : ~3 % des bits perturbés (1 bit sur ~32 dans 1 trame sur 3).
        var reencoded = PerturbBits(original, flipEveryNthFrame: 3);

        ChromaprintMatcher.Similarity(original, reencoded).Should()
            .BeGreaterThanOrEqualTo(ChromaprintMatcher.DefaultMinSimilarity);
    }

    [Fact]
    public void Similarity_DifferentTracks_IsLow()
    {
        var a = Fingerprint(seed: 1, length: 300);
        var b = Fingerprint(seed: 9999, length: 300);

        ChromaprintMatcher.Similarity(a, b).Should()
            .BeLessThan(ChromaprintMatcher.DefaultMinSimilarity);
    }

    [Fact]
    public void Similarity_HandlesLeadingSilenceOffset()
    {
        var original = Fingerprint(seed: 3, length: 300);
        // Même piste décalée de 20 trames (silence de tête) — l'alignement doit la retrouver.
        var shifted = Shift(original, by: 20);

        ChromaprintMatcher.Similarity(original, shifted).Should()
            .BeGreaterThanOrEqualTo(ChromaprintMatcher.DefaultMinSimilarity);
    }

    [Fact]
    public void Similarity_TooShortOverlap_IsZero()
    {
        var a = new AudioFingerprint(new uint[] { 1, 2, 3 });
        var b = new AudioFingerprint(new uint[] { 1, 2, 3 });

        // Recouvrement minimal (10) non atteint → 0.
        ChromaprintMatcher.Similarity(a, b, maxOffset: 0, minOverlap: 10).Should().Be(0.0);
    }

    // --- Générateurs déterministes ------------------------------------------

    private static AudioFingerprint Fingerprint(int seed, int length)
    {
        // Générateur congruentiel linéaire déterministe (pas de Random, interdit en script,
        // et reproductible ici).
        var subs = new uint[length];
        var state = (uint)seed * 2654435761u + 1u;
        for (var i = 0; i < length; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            subs[i] = state;
        }

        return new AudioFingerprint(subs);
    }

    private static AudioFingerprint PerturbBits(AudioFingerprint source, int flipEveryNthFrame)
    {
        var subs = source.SubFingerprints.ToArray();
        for (var i = 0; i < subs.Length; i += flipEveryNthFrame)
        {
            subs[i] ^= 1u << (i % 32); // bascule un seul bit
        }

        return new AudioFingerprint(subs);
    }

    private static AudioFingerprint Shift(AudioFingerprint source, int by)
    {
        var subs = new uint[source.Length + by];
        for (var i = 0; i < by; i++)
        {
            subs[i] = 0xABCDEF01u; // « silence » en tête
        }

        for (var i = 0; i < source.Length; i++)
        {
            subs[i + by] = source.SubFingerprints[i];
        }

        return new AudioFingerprint(subs);
    }
}
