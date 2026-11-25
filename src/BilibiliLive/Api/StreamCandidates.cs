namespace BilibiliLive.Api;

internal static class StreamCandidates
{
	public static LiveStreamListing Select(RoomPlayInfoData data)
	{
		return new LiveStreamListing(SelectCandidates(data), GetQualityNames(data));
	}

	private static List<LiveStreamCandidate.Listed> SelectCandidates(RoomPlayInfoData data)
	{
		List<LiveStreamCandidate.Listed> candidates = [];

		foreach (PlayStream stream in data.PlayurlInfo?.Playurl?.Stream ?? [])
		{
			if (ParseProtocol(stream.ProtocolName) is not { } protocol)
			{
				continue;
			}

			foreach (PlayFormat format in stream.Format ?? [])
			{
				if (ParseFormat(format.FormatName) is not { } streamFormat)
				{
					continue;
				}

				foreach (PlayCodec codec in format.Codec ?? [])
				{
					if (ParseCodec(codec.CodecName) is not { } streamCodec || codec.BaseUrl is null)
					{
						continue;
					}

					Uri[] urls = [.. GetUrls(codec.BaseUrl, codec.UrlInfo ?? [])];

					if (urls.Length is not 0)
					{
						candidates.Add(new LiveStreamCandidate.Listed(protocol, streamFormat, streamCodec, codec.CurrentQn, codec.AcceptQn ?? [], urls));
					}
				}
			}
		}

		return candidates;
	}

	private static Dictionary<long, string> GetQualityNames(RoomPlayInfoData data)
	{
		Dictionary<long, string> names = [];

		foreach (QualityDescription description in data.PlayurlInfo?.Playurl?.GQnDesc ?? [])
		{
			if (description.Desc is { Length: > 0 } name)
			{
				names.TryAdd(description.Qn, name);
			}
		}

		return names;
	}

	private static IEnumerable<Uri> GetUrls(string baseUrl, IReadOnlyList<PlayUrlHost> hosts)
	{
		foreach (PlayUrlHost host in hosts)
		{
			if (host.Host is { } origin
				&& origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
				&& Uri.TryCreate(origin + baseUrl + host.Extra, UriKind.Absolute, out Uri? uri))
			{
				yield return uri;
			}
		}
	}

	private static StreamProtocol? ParseProtocol(string? name)
	{
		return name switch
		{
			"http_stream" => StreamProtocol.HttpStream,
			"http_hls" => StreamProtocol.HttpHls,
			_ => null
		};
	}

	private static StreamFormat? ParseFormat(string? name)
	{
		return name switch
		{
			"flv" => StreamFormat.Flv,
			"ts" => StreamFormat.Ts,
			"fmp4" => StreamFormat.Fmp4,
			_ => null
		};
	}

	private static StreamCodec? ParseCodec(string? name)
	{
		return name switch
		{
			"avc" => StreamCodec.Avc,
			"hevc" => StreamCodec.Hevc,
			"av1" => StreamCodec.Av1,
			_ => null
		};
	}
}
