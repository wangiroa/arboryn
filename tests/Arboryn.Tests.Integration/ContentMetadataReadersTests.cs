using System.IO.Compression;
using System.Text;
using Arboryn.Application.Abstractions;
using Arboryn.Application.UseCases;
using Arboryn.Domain.ValueObjects;
using Arboryn.Infrastructure.FileSystem;
using Arboryn.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Arboryn.Tests.Integration;

/// <summary>
/// Tests des adapters <see cref="IContentMetadataReader"/> de l'Inc 4 (hors audio,
/// couvert par <see cref="TagLibAudioMetadataReaderTests"/>), sur des fichiers
/// minimaux mais valides construits à la volée.
/// </summary>
public class ContentMetadataReadersTests
{
    [Fact]
    public async Task Pdf_ExtractsInfoDictionary()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "rapport.pdf");
        await File.WriteAllBytesAsync(path, BuildMinimalPdf("Rapport Annuel", "Jean Dupont", "20230115120000"));

        var reader = new PdfDocumentMetadataReader();
        var values = await reader.ReadAsync(FilePath.From(path), CancellationToken.None);

        values[MetadataKeys.Title].Should().Be("Rapport Annuel");
        values[MetadataKeys.Author].Should().Be("Jean Dupont");
        values[MetadataKeys.Year].Should().Be("2023");
    }

    [Fact]
    public async Task Epub_ExtractsOpfMetadata()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "livre.epub");
        BuildMinimalEpub(path);

        var reader = new EpubMetadataReader();
        var values = await reader.ReadAsync(FilePath.From(path), CancellationToken.None);

        values[MetadataKeys.Title].Should().Be("Le Seigneur des Anneaux");
        values[MetadataKeys.Author].Should().Be("J.R.R. Tolkien");
        values[MetadataKeys.Language].Should().Be("fr");
        values[MetadataKeys.Publisher].Should().Be("Christian Bourgois");
        values[MetadataKeys.Isbn].Should().Be("9782070612888");
        values[MetadataKeys.Year].Should().Be("1954");
    }

    [Fact]
    public async Task Image_ExtractsDimensions()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "photo.png");
        await File.WriteAllBytesAsync(path, OnePixelPng());

        var reader = new ImageMetadataReader();
        var values = await reader.ReadAsync(FilePath.From(path), CancellationToken.None);

        values[MetadataKeys.Width].Should().Be("1");
        values[MetadataKeys.Height].Should().Be("1");
    }

    [Fact]
    public async Task Handler_FusesContentOverFilename_EndToEnd()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        // Le nom de fichier suggère un titre « Brouillon » ; l'OPF dit autre chose.
        var path = Path.Combine(temp.Path, "Brouillon (2019).epub");
        BuildMinimalEpub(path);

        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var instances = new SqliteFileInstanceRepository(db.Factory);
        var id = await instances.UpsertAsync(
            new FileInstanceRecord(
                FileInstanceId.New(), VolumeId.Default, FilePath.From(path),
                CanonicalName.From("Brouillon (2019).epub"), 1, DateTime.UtcNow),
            CancellationToken.None);

        var handler = new ExtractMetadataHandler(
            metadata,
            new IContentMetadataReader[]
            {
                new TagLibAudioMetadataReader(),
                new PdfDocumentMetadataReader(),
                new EpubMetadataReader(),
                new ImageMetadataReader(),
            },
            NullLogger<ExtractMetadataHandler>.Instance);

        await handler.ExecuteAsync(id, FilePath.From(path), CancellationToken.None);

        var fused = await metadata.GetFusedAsync(id, CancellationToken.None);

        // L'OPF (confiance 0.9) l'emporte sur le nom de fichier (0.5) pour le titre.
        fused[MetadataKeys.Title].Value.Should().Be("Le Seigneur des Anneaux");
        fused[MetadataKeys.Title].Source.Should().Be(MetadataSources.EpubOpf);
        fused[MetadataKeys.Author].Value.Should().Be("J.R.R. Tolkien");
        fused[MetadataKeys.Isbn].Value.Should().Be("9782070612888");
    }

    [Fact]
    public async Task Pdf_WithIsbnInSubject_ExposesIsbn()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "ebook.pdf");
        await File.WriteAllBytesAsync(
            path, BuildMinimalPdf("Un Roman", "Une Autrice", "20210101000000", subject: "ISBN 978-2-07-061288-8"));

        var reader = new PdfDocumentMetadataReader();
        var values = await reader.ReadAsync(FilePath.From(path), CancellationToken.None);

        values[MetadataKeys.Isbn].Should().Be("9782070612888");
    }

    [Fact]
    public async Task Scan_PdfEbookWithIsbn_RefinesLogicalFileToBook()
    {
        await using var db = await TestDatabase.CreateAsync();
        using var temp = new TempDir();

        // PDF avec ISBN → catégorie préliminaire « other_document » affinée en « book ».
        temp.WriteBytes(
            "ebook.pdf",
            BuildMinimalPdf("Un Roman", "Une Autrice", "20210101000000", subject: "ISBN 978-2-07-061288-8"));
        // PDF sans ISBN → reste « other_document ».
        temp.WriteBytes("facture.pdf", BuildMinimalPdf("Facture", "EDF", "20230101000000"));

        var instances = new SqliteFileInstanceRepository(db.Factory);
        var logicalFiles = new SqliteLogicalFileRepository(db.Factory);
        var metadata = new SqliteFileMetadataRepository(db.Factory);
        var extractor = new ExtractMetadataHandler(
            metadata,
            new IContentMetadataReader[] { new PdfDocumentMetadataReader() },
            NullLogger<ExtractMetadataHandler>.Instance);
        var scanHandler = new ScanDirectoryHandler(
            new FileScanner(NullLogger<FileScanner>.Instance),
            instances, logicalFiles, extractor, NullLogger<ScanDirectoryHandler>.Instance);

        await scanHandler.ExecuteAsync(FilePath.From(temp.Path), VolumeId.Default);

        await using var connection = await db.Factory.OpenAsync();
        var ebookCategory = await connection.ExecuteScalarAsync<string>(
            "SELECT lf.category FROM logical_files lf JOIN file_instances fi ON fi.logical_file_id = lf.id WHERE fi.relative_path = @P;",
            new { P = Path.Combine(temp.Path, "ebook.pdf") });
        ebookCategory.Should().Be("book");

        var invoiceCategory = await connection.ExecuteScalarAsync<string>(
            "SELECT lf.category FROM logical_files lf JOIN file_instances fi ON fi.logical_file_id = lf.id WHERE fi.relative_path = @P;",
            new { P = Path.Combine(temp.Path, "facture.pdf") });
        invoiceCategory.Should().Be("other_document");
    }

    // -------------------------------------------------------------------------
    // Constructeurs de fichiers d'échantillon
    // -------------------------------------------------------------------------

    /// <summary>
    /// Construit un PDF minimal valide (catalogue + page + dictionnaire Info), avec
    /// table xref aux offsets calculés. PdfPig lit le dictionnaire Info.
    /// </summary>
    private static byte[] BuildMinimalPdf(string title, string author, string creationDate, string? subject = null)
    {
        var info = subject is null
            ? $"<< /Title ({title}) /Author ({author}) /CreationDate (D:{creationDate}) >>"
            : $"<< /Title ({title}) /Author ({author}) /Subject ({subject}) /CreationDate (D:{creationDate}) >>";

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>",
            info,
        };

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");

        var offsets = new int[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i] = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append("xref\n").Append("0 ").Append(objects.Length + 1).Append('\n');
        sb.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            sb.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        sb.Append("trailer\n")
          .Append("<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R /Info 4 0 R >>\n")
          .Append("startxref\n").Append(xrefOffset).Append('\n')
          .Append("%%EOF");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>Construit un EPUB 3 minimal valide (mimetype + container + OPF + nav + chapitre).</summary>
    private static void BuildMinimalEpub(string path)
    {
        const string containerXml =
            "<?xml version=\"1.0\"?>\n" +
            "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\n" +
            "  <rootfiles>\n" +
            "    <rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/>\n" +
            "  </rootfiles>\n" +
            "</container>\n";

        const string opf =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\">\n" +
            "  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n" +
            "    <dc:identifier id=\"bookid\">urn:isbn:9782070612888</dc:identifier>\n" +
            "    <dc:title>Le Seigneur des Anneaux</dc:title>\n" +
            "    <dc:creator>J.R.R. Tolkien</dc:creator>\n" +
            "    <dc:language>fr</dc:language>\n" +
            "    <dc:publisher>Christian Bourgois</dc:publisher>\n" +
            "    <dc:date>1954</dc:date>\n" +
            "    <meta property=\"dcterms:modified\">2020-01-01T00:00:00Z</meta>\n" +
            "  </metadata>\n" +
            "  <manifest>\n" +
            "    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n" +
            "    <item id=\"ch1\" href=\"ch1.xhtml\" media-type=\"application/xhtml+xml\"/>\n" +
            "  </manifest>\n" +
            "  <spine>\n" +
            "    <itemref idref=\"ch1\"/>\n" +
            "  </spine>\n" +
            "</package>\n";

        const string nav =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n" +
            "<head><title>Nav</title></head>\n" +
            "<body><nav epub:type=\"toc\"><ol><li><a href=\"ch1.xhtml\">Chapitre 1</a></li></ol></nav></body>\n" +
            "</html>\n";

        const string chapter =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Chapitre 1</title></head>" +
            "<body><p>Texte</p></body></html>\n";

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        // mimetype en premier, non compressé (requis par la spec EPUB).
        var mimetype = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var w = new StreamWriter(mimetype.Open(), new UTF8Encoding(false)))
        {
            w.Write("application/epub+zip");
        }

        WriteEntry(archive, "META-INF/container.xml", containerXml);
        WriteEntry(archive, "OEBPS/content.opf", opf);
        WriteEntry(archive, "OEBPS/nav.xhtml", nav);
        WriteEntry(archive, "OEBPS/ch1.xhtml", chapter);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var w = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    /// <summary>PNG 1×1 valide (signature + IHDR + IDAT + IEND), pour vérifier la lecture des dimensions.</summary>
    private static byte[] OnePixelPng() => new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
        0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01,
        0x0D, 0x0A, 0x2D, 0xB4,
        0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    };
}
