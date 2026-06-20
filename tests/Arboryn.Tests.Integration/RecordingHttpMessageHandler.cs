using System.Net;

namespace Arboryn.Tests.Integration;

/// <summary>
/// <see cref="HttpMessageHandler"/> de test : enregistre chaque requête sortante et renvoie une
/// réponse JSON canned selon l'URL. Permet de tester les providers sans réseau et d'auditer ce
/// qui sort (garantie privacy-first : aucun nom de fichier / chemin dans la requête).
/// </summary>
internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<Uri, (HttpStatusCode Status, string Json)> _respond;

    public RecordingHttpMessageHandler(Func<Uri, (HttpStatusCode, string)> respond)
        => _respond = respond;

    /// <summary>Convenance : réponse 200 fixe quelle que soit l'URL.</summary>
    public RecordingHttpMessageHandler(string json)
        : this(_ => (HttpStatusCode.OK, json))
    {
    }

    public List<Uri> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        var (status, json) = _respond(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
