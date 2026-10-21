using System.Net;
using System.Text;

namespace JtsCommandCenter.Tests.Support;

/// <summary>
/// Serves canned responses by URL substring so services can be tested without the network. The first
/// matching rule wins; anything unmatched gets a 404.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly List<(string UrlPart, HttpStatusCode Status, string Body, string MediaType)> rules = [];

    public List<string> Requests { get; } = [];

    public FakeHttpHandler Json(string urlPart, string body) => Add(urlPart, HttpStatusCode.OK, body, "application/json");

    public FakeHttpHandler Xml(string urlPart, string body) => Add(urlPart, HttpStatusCode.OK, body, "application/xml");

    public FakeHttpHandler Fail(string urlPart, HttpStatusCode status = HttpStatusCode.InternalServerError) =>
        Add(urlPart, status, "", "text/plain");

    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri("https://site.api.espn.com/") };

    private FakeHttpHandler Add(string urlPart, HttpStatusCode status, string body, string mediaType)
    {
        rules.Add((urlPart, status, body, mediaType));
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();
        Requests.Add(url);

        foreach (var (urlPart, status, body, mediaType) in rules)
        {
            if (url.Contains(urlPart, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, mediaType)
                });
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
