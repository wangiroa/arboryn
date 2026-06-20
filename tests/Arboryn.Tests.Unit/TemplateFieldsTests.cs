using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Unit;

public class TemplateFieldsTests
{
    [Fact]
    public void From_AddsExtension_WithoutDotAndLowercased()
    {
        var fields = TemplateFields.From(MediaCategory.Audiobook, new Dictionary<string, string>(), ".M4B");
        fields["ext"].Should().Be("m4b");
    }

    [Fact]
    public void From_DerivesAuthor_FromAlbumArtist_WhenAuthorMissing()
    {
        var fused = new Dictionary<string, string> { [MetadataKeys.AlbumArtist] = "Asimov" };
        var fields = TemplateFields.From(MediaCategory.Audiobook, fused, ".m4b");
        fields[MetadataKeys.Author].Should().Be("Asimov");
    }

    [Fact]
    public void From_KeepsExplicitAuthor_OverAlias()
    {
        var fused = new Dictionary<string, string>
        {
            [MetadataKeys.Author] = "Tolkien",
            [MetadataKeys.AlbumArtist] = "Narrateur",
        };
        var fields = TemplateFields.From(MediaCategory.Book, fused, ".epub");
        fields[MetadataKeys.Author].Should().Be("Tolkien");
    }

    [Fact]
    public void From_DerivesTitle_FromAlbum_WhenTitleMissing()
    {
        var fused = new Dictionary<string, string> { [MetadataKeys.Album] = "Fondation" };
        var fields = TemplateFields.From(MediaCategory.Audiobook, fused, ".m4b");
        fields[MetadataKeys.Title].Should().Be("Fondation");
    }

    [Fact]
    public void From_DerivesTitle_FromParentDirectory_WhenFilenameIsPositionOnly()
    {
        // Cas signalé : « 001 chapitre 1.mp3 » dans « Histoire totale… - Olivier Wieviorka ».
        var fused = new Dictionary<string, string>
        {
            [MetadataKeys.Author] = "Olivier Wieviorka",
            [MetadataKeys.Title] = "001 chapitre 1", // titre tiré du nom de fichier
        };

        var fields = TemplateFields.From(
            MediaCategory.Audiobook, fused, ".mp3",
            fileStem: "001 chapitre 1",
            parentDirectoryName: "Histoire totale de la seconde guerre mondiale - Olivier Wieviorka",
            chapterNumber: "001"); // numéro zero-paddé fourni par le planificateur

        fields[MetadataKeys.Title].Should().Be("Histoire totale de la seconde guerre mondiale");
        fields[MetadataKeys.Chapter].Should().Be("001");
    }

    [Fact]
    public void From_DerivesFromDirectory_EvenWhenTitleTagCarriesWorkTitle()
    {
        // Régression : beaucoup d'audiobooks répètent le titre de l'œuvre dans le tag Title de
        // chaque piste. Le nom de fichier positionnel doit primer → titre du dossier + numéro,
        // sinon toutes les pistes deviennent « Auteur - Titre » et collisionnent (« (2) »…).
        var fused = new Dictionary<string, string>
        {
            [MetadataKeys.AlbumArtist] = "Olivier Wieviorka",
            [MetadataKeys.Album] = "Histoire totale de la Seconde Guerre mondiale",
            [MetadataKeys.Title] = "Histoire totale de la Seconde Guerre mondiale", // tag trompeur
        };

        var fields = TemplateFields.From(
            MediaCategory.Audiobook, fused, ".mp3",
            fileStem: "001 Chapitre 1",
            parentDirectoryName: "Histoire totale de la Seconde Guerre mondiale - Olivier Wieviorka",
            chapterNumber: "001");

        fields[MetadataKeys.Title].Should().Be("Histoire totale de la Seconde Guerre mondiale");
        fields[MetadataKeys.Author].Should().Be("Olivier Wieviorka");
        fields[MetadataKeys.Chapter].Should().Be("001");
    }

    [Fact]
    public void From_KeepsFileTitle_WhenFilenameCarriesOwnIdentity()
    {
        // Œuvre mono-fichier : le fichier porte un vrai titre → pas de bascule sur le dossier.
        var fused = new Dictionary<string, string>
        {
            [MetadataKeys.Author] = "Asimov",
            [MetadataKeys.Title] = "Fondation",
        };

        var fields = TemplateFields.From(
            MediaCategory.Audiobook, fused, ".m4b",
            fileStem: "Asimov - Fondation",
            parentDirectoryName: "incoming");

        fields[MetadataKeys.Title].Should().Be("Fondation");
        fields.Should().NotContainKey(MetadataKeys.Chapter);
    }

    [Fact]
    public void From_DoesNotDeriveFromDirectory_ForNonMultiFileCategory()
    {
        // Les vidéos ne sont pas des œuvres multi-fichiers : pas de bascule sur le dossier.
        var fused = new Dictionary<string, string> { [MetadataKeys.Title] = "001" };

        var fields = TemplateFields.From(
            MediaCategory.Video, fused, ".mkv",
            fileStem: "001",
            parentDirectoryName: "Mon Film");

        fields[MetadataKeys.Title].Should().Be("001");
        fields.Should().NotContainKey(MetadataKeys.Chapter);
    }
}
