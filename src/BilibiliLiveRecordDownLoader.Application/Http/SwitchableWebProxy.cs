namespace BilibiliLiveRecordDownLoader.Application.Http;

internal sealed class SwitchableWebProxy(IWebProxy systemProxy) : IWebProxy
{
	private volatile AppConfig? _config;

	public ICredentials? Credentials
	{
		get => systemProxy.Credentials;
		set => systemProxy.Credentials = value;
	}

	private bool Enabled => _config?.UseSystemProxy is true;

	public void Follow(AppConfig config)
	{
		_config = config;
	}

	public Uri? GetProxy(Uri destination)
	{
		return Enabled ? systemProxy.GetProxy(destination) : null;
	}

	public bool IsBypassed(Uri host)
	{
		return !Enabled || systemProxy.IsBypassed(host);
	}
}
