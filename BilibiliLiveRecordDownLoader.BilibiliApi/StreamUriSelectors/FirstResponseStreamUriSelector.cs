namespace BilibiliApi.StreamUriSelectors;

public class FirstResponseStreamUriSelector : IStreamUriSelector
{
	public HttpClient? Client { get; set; }

	public async ValueTask<Uri> GetUriAsync(IEnumerable<Uri> uris, CancellationToken cancellationToken = default)
	{
		if (Client is null)
		{
			throw new InvalidOperationException(@"HttpClient not set!");
		}

		foreach (Uri uri in uris)
		{
			try
			{
				await using Stream _ = await Client.GetStreamAsync(uri, cancellationToken);

				return uri;
			}
			catch
			{
				// ignored
			}
		}

		throw new HttpRequestException(@"没有可用的直播地址");
	}
}
