using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using FluentAssertions;
using Xunit;

namespace Arboryn.Tests.Integration;

public class TagLibAudioMetadataReaderTests
{
    [Fact]
    public async Task Read_RoundTripsTagsFromGeneratedMp3()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"Arboryn-mp3-{Guid.NewGuid():N}.mp3");
        await File.WriteAllBytesAsync(path, BuildMinimalMp3());

        // Écrit les tags via TagLib (qui ajoutera un header ID3v2 au fichier).
        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Title = "Hamlet Act 1";
            file.Tag.Performers = new[] { "Shakespeare" };
            file.Tag.Album = "Tragedies";
            file.Tag.Year = 1601;
            file.Tag.Track = 3;
            file.Save();
        }

        try
        {
            var reader = new TagLibAudioMetadataReader();
            var values = await reader.ReadAsync(FilePath.From(path), CancellationToken.None);

            values[MetadataKeys.Title].Should().Be("Hamlet Act 1");
            values[MetadataKeys.Artist].Should().Be("Shakespeare");
            values[MetadataKeys.Album].Should().Be("Tragedies");
            values[MetadataKeys.Year].Should().Be("1601");
            values[MetadataKeys.TrackNumber].Should().Be("3");
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Construit un MP3 minimal valide : plusieurs frames MPEG-1 Layer III mono
    /// 32 kbps 44,1 kHz remplies de zéros (silencieuses). TagLib accepte ce contenu
    /// comme audio MP3 et peut y écrire des tags ID3v2.
    /// </summary>
    private static byte[] BuildMinimalMp3()
    {
        var bytes = new List<byte>(capacity: 104 * 8);
        for (var i = 0; i < 8; i++)
        {
            // Header : 0xFF 0xFB 0x10 0xC0
            //   FF FB = sync + MPEG-1 + Layer III + no CRC
            //   10    = bitrate index 1 (32 kbps) + sample rate index 0 (44.1 kHz)
            //   C0    = mono + emphasis none
            bytes.Add(0xFF);
            bytes.Add(0xFB);
            bytes.Add(0x10);
            bytes.Add(0xC0);
            // Reste de la frame (104 - 4 = 100 octets) en silence.
            for (var j = 0; j < 100; j++)
            {
                bytes.Add(0);
            }
        }
        return bytes.ToArray();
    }
}
