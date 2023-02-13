namespace BilibiliApi.Model.PlayUrl;

public class RoomPlayInfo
{
	/// <summary>
	/// 正常返回 0
	/// </summary>
	public long code { get; set; }

	/// <summary>
	/// 正常返回 "0"，否则返回错误信息
	/// </summary>
	public string? message { get; set; }

	/// <summary>
	/// 直播播放地址信息
	/// </summary>
	public Data? data { get; set; }

	public class Data
	{
		public PlayurlInfo? playurl_info { get; set; }

		public class PlayurlInfo
		{
			public Playurl? playurl { get; set; }

			public class Playurl
			{
				public Stream[]? stream { get; set; }

				public class Stream
				{
					public string? protocol_name { get; set; }
					public Format[]? format { get; set; }

					public class Format
					{
						public string? format_name { get; set; }
						public Codec[]? codec { get; set; }

						public class Codec
						{
							public string? codec_name { get; set; }
							public long current_qn { get; set; }
							public string? base_url { get; set; }
							public UrlInfo[]? url_info { get; set; }

							public class UrlInfo
							{
								public string? host { get; set; }
								public string? extra { get; set; } }
						}
					}
				}
			}
		}
	}
}
