using System.Diagnostics;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed class HangingHandler : HttpMessageHandler
{
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.Infinite, cancellationToken);
		throw new UnreachableException();
	}
}

internal sealed class HangingHttpClientFactory : IHttpClientFactory
{
	public HttpClient CreateClient(string name)
	{
		return new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(100) };
	}
}
