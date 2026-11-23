using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace BilibiliLive.Tests.Fakes;

internal sealed record RecordedRequest(Uri Uri, IReadOnlyDictionary<string, string> Headers);

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
	public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

	public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : this((request, _) => Task.FromResult(respond(request)))
	{
	}

	public IEnumerable<RecordedRequest> RequestsTo(string path)
	{
		return Requests.Where(r => r.Uri.AbsolutePath == path);
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		Uri uri = request.RequestUri ?? throw new InvalidOperationException("The request has no uri.");
		Requests.Enqueue(new RecordedRequest(uri, request.Headers.NonValidated.ToDictionary(static h => h.Key, static h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase)));

		HttpResponseMessage response = await respond(request, cancellationToken);
		response.RequestMessage ??= request;
		return response;
	}
}

internal static class Responses
{
	public static HttpResponseMessage Json(string json)
	{
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
	}

	public static HttpResponseMessage Fixture(string name)
	{
		return Json(FixtureText(name));
	}

	public static string FixtureText(string name)
	{
		return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
	}

	public static HttpResponseMessage Text(string text)
	{
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) };
	}

	public static HttpResponseMessage Bytes(byte[] bytes)
	{
		return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
	}

	public static HttpResponseMessage Status(HttpStatusCode statusCode)
	{
		return new HttpResponseMessage(statusCode);
	}

	public static HttpResponseMessage NotFound()
	{
		return Status(HttpStatusCode.NotFound);
	}
}
