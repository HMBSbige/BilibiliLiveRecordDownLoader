using DynamicData.Kernel;
using System.Security.Cryptography;

namespace BilibiliApi.StreamUriSelectors;

public class RandomStreamUriSelector : IStreamUriSelector
{
	public HttpClient? Client { get; set; }

	public ValueTask<Uri> GetUriAsync(IEnumerable<Uri> uris, CancellationToken cancellationToken = default)
	{
		Uri[] list = uris.AsArray();

		if (list.Length < 1)
		{
			throw new HttpRequestException(@"没有可用的直播地址");
		}

		return ValueTask.FromResult(list[RandomNumberGenerator.GetInt32(list.Length)]);
	}
}
