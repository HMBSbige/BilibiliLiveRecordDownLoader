namespace BilibiliLiveRecordDownLoader.Application.Http;

// 每次请求现读共享配置里的开关，设置页一改就生效；Follow 之前一律直连
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
