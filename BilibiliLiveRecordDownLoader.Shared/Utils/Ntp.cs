using Dns.Net.Clients;
using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace BilibiliLiveRecordDownLoader.Shared.Utils;

public static class Ntp
{
	private static readonly DateTime BaseTime = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	private static readonly DefaultDnsClient DnsClient = new();

	public static async ValueTask<DateTime> GetWebTimeAsync(IPEndPoint server, CancellationToken cancellationToken = default)
	{
		// NTP message size - 16 bytes of the digest (RFC 2030)
		using IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(48);
		Memory<byte> ntpData = memoryOwner.Memory;
		// Setting the Leap Indicator, Version Number and Mode values
		ntpData.Span[0] = 0x1B;// LI = 0 (no warning), VN = 3 (IPv4 only), Mode = 3 (Client Mode)

		using Socket socket = new(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
		await socket.ConnectAsync(server, cancellationToken);

		await socket.SendAsync(ntpData[..48], SocketFlags.None, cancellationToken);

		await socket.ReceiveAsync(ntpData, SocketFlags.None, cancellationToken);

		const byte serverReplyTime = 40;
		uint integer = BinaryPrimitives.ReadUInt32BigEndian(ntpData.Span[serverReplyTime..]);
		uint fraction = BinaryPrimitives.ReadUInt32BigEndian(ntpData.Span[(serverReplyTime + 4)..]);
		long milliseconds = integer * 1000L + fraction * 1000L / 0x100000000L;

		return BaseTime.AddMilliseconds(milliseconds);
	}

	public static async ValueTask<DateTime> GetCurrentTimeAsync()
	{
		try
		{
			using CancellationTokenSource cts = new(TimeSpan.FromSeconds(3));
			IPAddress ip = await DnsClient.QueryAsync(@"ntp.bige0.com", cts.Token);

			return await GetWebTimeAsync(new IPEndPoint(ip, 123), cts.Token);
		}
		catch
		{
			// ignored
		}

		return DateTime.UtcNow;
	}
}
