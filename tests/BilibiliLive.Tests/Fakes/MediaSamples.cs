using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace BilibiliLive.Tests.Fakes;

/// <summary>能通过结构校验的最小媒体，带一个可读的 [marker]，用来在输出里区分各个分片。</summary>
internal static partial class MediaSamples
{
	private const int PacketSize = 188;

	public static byte[] TransportStream(string marker, int packets = 3)
	{
		byte[] data = new byte[PacketSize * packets];
		data.AsSpan().Fill(0xFF);

		for (int packet = 0; packet < packets; ++packet)
		{
			data[packet * PacketSize] = 0x47;
		}

		Marker(marker).CopyTo(data.AsSpan(4));
		return data;
	}

	public static byte[] Fmp4Init(string marker)
	{
		return [.. Box("ftyp", Marker(marker)), .. Box("moov", [])];
	}

	public static byte[] Fmp4Fragment(string marker)
	{
		return [.. Box("moof", Marker(marker)), .. Box("mdat", [])];
	}

	public static byte[] ForPath(string path, string marker)
	{
		if (Path.GetExtension(path) is not ".m4s")
		{
			return TransportStream(marker);
		}

		return Path.GetFileName(path).StartsWith('h') ? Fmp4Init(marker) : Fmp4Fragment(marker);
	}

	public static string Markers(byte[] output)
	{
		return string.Concat(MarkerPattern.Matches(Encoding.Latin1.GetString(output)).Select(static match => match.Value));
	}

	public static byte[] Box(string type, byte[] payload)
	{
		byte[] box = new byte[8 + payload.Length];
		BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
		Encoding.ASCII.GetBytes(type, box.AsSpan(4));
		payload.CopyTo(box.AsSpan(8));
		return box;
	}

	private static byte[] Marker(string marker)
	{
		return Encoding.ASCII.GetBytes($"[{marker}]");
	}

	[GeneratedRegex(@"\[[^\[\]]*\]")]
	private static partial Regex MarkerPattern { get; }
}
