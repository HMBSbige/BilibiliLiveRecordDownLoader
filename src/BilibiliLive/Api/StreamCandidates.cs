namespace BilibiliLive.Api;

internal static class StreamCandidates
{
	public static LiveStreamListing Select(RoomPlayInfoData data)
	{
		PlayUrl? playUrl = data.PlayurlInfo?.Playurl;
		return new LiveStreamListing(SelectCandidates(playUrl?.Stream ?? []), GetQualityNames(playUrl?.GQnDesc ?? []));
	}

	private static List<LiveStreamCandidate.Listed> SelectCandidates(IReadOnlyList<PlayStream> streams)
	{
		List<LiveStreamCandidate.Listed> candidates = [];

		foreach (PlayStream stream in streams)
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

					List<Uri> urls = GetUrls(codec.BaseUrl, codec.UrlInfo ?? []);

					if (urls.Count is not 0)
					{
						candidates.Add(new LiveStreamCandidate.Listed(protocol, streamFormat, streamCodec, codec.CurrentQn, codec.AcceptQn ?? [], urls) { StreamName = LiveStreamName.Parse(urls[0].AbsolutePath) });
					}
				}
			}
		}

		return candidates;
	}

	private static Dictionary<long, string> GetQualityNames(IReadOnlyList<QualityDescription> descriptions)
	{
		Dictionary<long, string> names = new(descriptions.Count);

		foreach (QualityDescription description in descriptions)
		{
			if (description.Desc is { Length: > 0 } name)
			{
				names.TryAdd(description.Qn, name);
			}
		}

		return names;
	}

	private static List<Uri> GetUrls(string baseUrl, IReadOnlyList<PlayUrlHost> hosts)
	{
		List<Uri> urls = new(hosts.Count);

		foreach (PlayUrlHost host in hosts)
		{
			if (host.Host is { } origin
				&& origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
				&& Uri.TryCreate(origin + baseUrl + host.Extra, UriKind.Absolute, out Uri? uri))
			{
				urls.Add(uri);
			}
		}

		return urls;
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
