namespace BilibiliApi.StreamUriSelectors;

public interface IStreamUriSelector
{
	HttpClient? Client { get; set; }

	ValueTask<Uri> GetUriAsync(IEnumerable<Uri> uris, CancellationToken cancellationToken = default);
}
