using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

namespace BilibiliApi.StreamUriSelectors;

public class FastestResponseStreamUriSelector : IStreamUriSelector
{
	public HttpClient? Client { get; set; }

	public async ValueTask<Uri> GetUriAsync(IEnumerable<Uri> uris, CancellationToken cancellationToken = default)
	{
		if (Client is null)
		{
			throw new InvalidOperationException(@"HttpClient not set!");
		}

		Uri? result = await uris.Select(uri => Observable.FromAsync(ct => Test(uri, ct))
				.Catch<Uri?, HttpRequestException>(_ => Observable.Return<Uri?>(null))
				.Where(r => r is not null)
			)
			.Merge()
			.FirstOrDefaultAsync()
			.ToTask(cancellationToken);

		return result ?? throw new HttpRequestException(@"没有可用的直播地址");

		async Task<Uri> Test(Uri uri, CancellationToken ct)
		{
			await using Stream _ = await Client.GetStreamAsync(uri, ct);
			return uri;
		}
	}
}
