namespace BilibiliLiveRecordDownLoader.Shared.HttpPolicy;

public class RetryHandler : DelegatingHandler
{
	private readonly uint _maxRetries;

	public RetryHandler(HttpMessageHandler innerHandler, uint maxRetries) : base(innerHandler)
	{
		_maxRetries = maxRetries;
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		HttpResponseMessage response;
		var i = 0;
		do
		{
			response = await base.SendAsync(request, cancellationToken);
			if (response.IsSuccessStatusCode)
			{
				return response;
			}
		} while (++i < _maxRetries);

		return response;
	}
}
