using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace BilibiliLive.Tests.Fakes;

/// <summary>Minimal media that passes structural validation while carrying a readable [marker] to tell segments apart in the output.</summary>
internal static partial class MediaSamples
{
	private const int PacketSize = 188;

	public static byte[] Flv(string marker)
	{
		return [.. "FLV"u8, .. Marker(marker)];
	}

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
		return Path.GetExtension(path) is ".m4s" ? Path.GetFileName(path).StartsWith('h') ? Fmp4Init(marker) : Fmp4Fragment(marker) : TransportStream(marker);
	}

	public static string Markers(byte[] output)
	{
		return string.Concat(MarkerPattern().Matches(Encoding.Latin1.GetString(output)).Select(match => match.Value));
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
	private static partial Regex MarkerPattern();
}
