using Arboryn.Application.Abstractions;
using Arboryn.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Tesseract;

namespace Arboryn.Infrastructure.FileSystem;

/// <summary>
/// OCR des documents scannés via Tesseract. Se dégrade proprement : <see cref="IsAvailable"/>
/// est faux (et <see cref="RecognizeAsync"/> renvoie <c>null</c>) si le dossier <c>tessdata</c>
/// ou les données de langue sont absents, ou si le runtime natif échoue. Le dossier tessdata
/// est résolu via la variable d'environnement <c>ARBORYN_TESSDATA</c>, puis <c>tessdata/</c>
/// à côté de l'exécutable.
/// </summary>
public sealed class TesseractOcrEngine : IOcrEngine
{
    private readonly ILogger<TesseractOcrEngine> _logger;
    private readonly string? _tessdataPath;
    private readonly string _languages;
    private int _warned;

    public TesseractOcrEngine(ILogger<TesseractOcrEngine> logger)
    {
        _logger = logger;
        _tessdataPath = ResolveTessdata();
        _languages = _tessdataPath is null ? string.Empty : ResolveLanguages(_tessdataPath);
    }

    public bool IsAvailable => _tessdataPath is not null && _languages.Length > 0;

    public Task<string?> RecognizeAsync(FilePath imagePath, CancellationToken cancellationToken)
        => Task.Run<string?>(() => Recognize(imagePath), cancellationToken);

    private string? Recognize(FilePath imagePath)
    {
        if (!IsAvailable)
        {
            return null;
        }

        try
        {
            using var engine = new TesseractEngine(_tessdataPath, _languages, EngineMode.Default);
            using var pix = Pix.LoadFromFile(imagePath.Value);
            using var page = engine.Process(pix);
            var text = page.GetText();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _warned, 1) == 0)
            {
                _logger.LogWarning(ex, "OCR Tesseract indisponible (runtime natif requis). Le triage continue sans OCR.");
            }

            return null;
        }
    }

    private static string? ResolveTessdata()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ARBORYN_TESSDATA");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
        {
            return fromEnv;
        }

        var beside = Path.Combine(AppContext.BaseDirectory, "tessdata");
        return Directory.Exists(beside) ? beside : null;
    }

    /// <summary>Préfère le français + l'anglais selon les <c>.traineddata</c> présents.</summary>
    private static string ResolveLanguages(string tessdataPath)
    {
        var langs = new List<string>();
        if (File.Exists(Path.Combine(tessdataPath, "fra.traineddata")))
        {
            langs.Add("fra");
        }

        if (File.Exists(Path.Combine(tessdataPath, "eng.traineddata")))
        {
            langs.Add("eng");
        }

        return string.Join('+', langs);
    }
}
