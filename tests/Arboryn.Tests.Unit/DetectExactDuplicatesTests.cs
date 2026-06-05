using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using Arboryn.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class DetectExactDuplicatesTests
{
    [Fact]
    public void Group_GroupsBySameCanonicalNameAndSize()
    {
        var records = new List<FileInstanceRecord>
        {
            Rec(@"C:\a\book.epub", "book.epub", 100),
            Rec(@"C:\b\book.epub", "book.epub", 100),
            Rec(@"C:\c\book.epub", "book.epub", 200), // taille différente → exclu
            Rec(@"C:\d\other.pdf", "other.pdf", 50),  // unique → exclu
        };

        var groups = DetectExactDuplicatesHandler.Group(records);

        groups.Should().HaveCount(1);
        groups[0].Kind.Should().Be(DuplicateGroupKind.ExactName);
        groups[0].Confidence.Should().Be(1.0);
        groups[0].Members.Should().HaveCount(2);
    }

    [Fact]
    public void Group_SeparatesGroupsByDifferingSize()
    {
        var records = new List<FileInstanceRecord>
        {
            Rec(@"C:\a\book.epub", "book.epub", 100),
            Rec(@"C:\b\book.epub", "book.epub", 100),
            Rec(@"C:\c\film.mkv", "film.mkv", 700),
            Rec(@"C:\d\film.mkv", "film.mkv", 700),
        };

        var groups = DetectExactDuplicatesHandler.Group(records);

        groups.Should().HaveCount(2);
        groups.Should().OnlyContain(g => g.Members.Count == 2);
    }

    [Fact]
    public void Group_NoDuplicates_ReturnsEmpty()
    {
        var records = new List<FileInstanceRecord> { Rec(@"C:\a\x.pdf", "x.pdf", 1) };

        DetectExactDuplicatesHandler.Group(records).Should().BeEmpty();
    }

    private static FileInstanceRecord Rec(string absolutePath, string canonical, long size) => new(
        FileInstanceId.New(),
        VolumeId.Default,
        FilePath.From(absolutePath),
        CanonicalName.From(canonical),
        size,
        DateTime.UtcNow);
}
