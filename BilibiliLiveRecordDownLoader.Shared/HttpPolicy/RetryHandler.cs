namespace BilibiliLiveRecordDownLoader.Shared.HttpPolicy;

public class RetryHandler(HttpMessageHandler innerHandler, uint maxRetries) : DelegatingHandler(innerHandler)
{
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		HttpResponseMessage response;
		int i = 0;

		do
		{
			response = await base.SendAsync(request, cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				return response;
			}
		} while (++i < maxRetries);

		return response;
	}
}
