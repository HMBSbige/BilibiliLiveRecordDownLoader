namespace BilibiliLiveRecordDownLoader.Application.Http;

internal sealed class SwitchableWebProxy : IWebProxy
{
	private static readonly IWebProxy SystemProxy = HttpClient.DefaultProxy;

	private volatile bool _enabled;

	public bool Enabled
	{
		get => _enabled;
		set => _enabled = value;
	}

	public ICredentials? Credentials
	{
		get => SystemProxy.Credentials;
		set => SystemProxy.Credentials = value;
	}

	public Uri? GetProxy(Uri destination)
	{
		return _enabled ? SystemProxy.GetProxy(destination) : null;
	}

	public bool IsBypassed(Uri host)
	{
		return !_enabled || SystemProxy.IsBypassed(host);
	}
}
