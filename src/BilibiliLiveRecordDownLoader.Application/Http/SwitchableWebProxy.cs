namespace BilibiliLiveRecordDownLoader.Application.Http;

internal sealed class SwitchableWebProxy(IWebProxy systemProxy) : IWebProxy
{
	private volatile bool _enabled;

	public bool Enabled
	{
		get => _enabled;
		set => _enabled = value;
	}

	public ICredentials? Credentials
	{
		get => systemProxy.Credentials;
		set => systemProxy.Credentials = value;
	}

	public Uri? GetProxy(Uri destination)
	{
		return _enabled ? systemProxy.GetProxy(destination) : null;
	}

	public bool IsBypassed(Uri host)
	{
		return !_enabled || systemProxy.IsBypassed(host);
	}
}
