using BilibiliLive.Http;
using BilibiliLive.Tests.Fakes;

namespace BilibiliLive.Tests;

internal static class ApiPaths
{
	public const string RoomBaseInfo = "/xlive/web-room/v1/index/getRoomBaseInfo";
	public const string RoomPlayInfo = "/xlive/web-room/v2/index/getRoomPlayInfo";
	public const string MasterPlaylist = "/xlive/play-gateway/master/url";
	public const string DanmuInfo = "/xlive/web-room/v1/index/getDanmuInfo";
	public const string Nav = "/x/web-interface/nav";
	public const string Following = "/xlive/web-ucenter/user/following";
	public const string QrCodeGenerate = "/x/passport-login/web/qrcode/generate";
	public const string QrCodePoll = "/x/passport-login/web/qrcode/poll";
}

internal sealed class ApiClient : IDisposable
{
	public ApiClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeProvider? timeProvider = null, BilibiliIdentity? identity = null)
	{
		Handler = new FakeHttpMessageHandler(respond);
		HttpClient = new HttpClient(identity is null ? Handler : new BilibiliRequestHandler(identity) { InnerHandler = Handler });
		Client = new BilibiliApiClient(HttpClient, timeProvider);
	}

	public ApiClient(Func<HttpRequestMessage, HttpResponseMessage> respond, TimeProvider? timeProvider = null) : this((request, _) => Task.FromResult(respond(request)), timeProvider)
	{
	}

	public FakeHttpMessageHandler Handler { get; }

	public HttpClient HttpClient { get; }

	public BilibiliApiClient Client { get; }

	public static ApiClient Routes(IReadOnlyDictionary<string, Func<HttpResponseMessage>> routes, TimeProvider? timeProvider = null)
	{
		return new ApiClient(request => routes.TryGetValue(request.RequestUri?.AbsolutePath ?? string.Empty, out Func<HttpResponseMessage>? route) ? route() : Responses.NotFound(), timeProvider);
	}

	public void Dispose()
	{
		HttpClient.Dispose();
	}
}
