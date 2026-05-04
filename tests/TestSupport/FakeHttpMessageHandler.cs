using System.Net;

namespace TestSupport;

public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    public void Enqueue(HttpStatusCode statusCode, string body = "")
    {
        _responses.Enqueue(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body)
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return await Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }

        Requests.Add(await CloneRequestAsync(request, cancellationToken));
        return _responses.Count == 0
            ? new HttpResponseMessage(HttpStatusCode.OK)
            : _responses.Dequeue();
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage source, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri);

        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (source.Content is not null)
        {
            var body = await source.Content.ReadAsStringAsync(cancellationToken);
            var clonedContent = new StringContent(body);

            foreach (var header in source.Content.Headers)
            {
                clonedContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = clonedContent;
        }

        return clone;
    }
}
