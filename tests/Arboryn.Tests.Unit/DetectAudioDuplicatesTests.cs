using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class DetectAudioDuplicatesTests
{
    [Fact]
    public void Group_ClustersSameTrack_ExcludesDifferentAndSingletons()
    {
        var track = Sequence(seed: 5, length: 250);
        var trackReencoded = Sequence(seed: 5, length: 250); // identique → similarité 1
        var different = Sequence(seed: 9999, length: 250);

        var items = new[]
        {
            Item("livre.mp3", track),
            Item("livre.flac", trackReencoded),
            Item("autre.mp3", different),
        };

        var groups = DetectAudioDuplicatesHandler.GroupCore(items, ChromaprintMatcher_DefaultMinSimilarity);

        groups.Should().HaveCount(1);
        groups[0].Members.Select(m => m.Instance.CanonicalName.Value)
            .Should().BeEquivalentTo("livre.mp3", "livre.flac");
    }

    [Fact]
    public void Group_DoesNotCompare_VeryDifferentDurations()
    {
        var shortTrack = Sequence(seed: 1, length: 40);
        var longTrack = Sequence(seed: 1, length: 400); // même graine mais 10× plus long

        var items = new[]
        {
            Item("court.mp3", shortTrack),
            Item("long.mp3", longTrack),
        };

        // Durées trop éloignées → pré-filtre, pas de groupe.
        DetectAudioDuplicatesHandler.GroupCore(items, ChromaprintMatcher_DefaultMinSimilarity)
            .Should().BeEmpty();
    }

    [Fact]
    public void GroupCore_RepresentativeIsStable_AcrossOrdering()
    {
        var a = Sequence(seed: 2, length: 200);
        var b = Sequence(seed: 2, length: 200);

        var forward = DetectAudioDuplicatesHandler.GroupCore(
            new[] { Item("a.mp3", a), Item("b.mp3", b) }, ChromaprintMatcher_DefaultMinSimilarity);
        var reversed = DetectAudioDuplicatesHandler.GroupCore(
            new[] { Item("b.mp3", b), Item("a.mp3", a) }, ChromaprintMatcher_DefaultMinSimilarity);

        forward.Should().HaveCount(1);
        reversed.Should().HaveCount(1);
        forward[0].Representative.StableDigest().Should().Be(reversed[0].Representative.StableDigest());
    }

    private const double ChromaprintMatcher_DefaultMinSimilarity = 0.90;

    private static AudioFingerprintedInstance Item(string name, AudioFingerprint fingerprint)
    {
        var record = new FileInstanceRecord(
            FileInstanceId.New(),
            VolumeId.Default,
            FilePath.From(@"C:\audio\" + name),
            CanonicalName.From(name),
            Size: 1000,
            ModifiedAt: DateTime.UnixEpoch);
        return new AudioFingerprintedInstance(record, fingerprint);
    }

    private static AudioFingerprint Sequence(int seed, int length)
    {
        var subs = new uint[length];
        var state = (uint)seed * 2654435761u + 1u;
        for (var i = 0; i < length; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            subs[i] = state;
        }

        return new AudioFingerprint(subs);
    }
}
