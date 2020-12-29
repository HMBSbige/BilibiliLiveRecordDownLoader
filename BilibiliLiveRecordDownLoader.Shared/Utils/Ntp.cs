using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Shared.Utils
{
	public static class Ntp
	{
		private static readonly DateTime BaseTime = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		public static async ValueTask<DateTime> GetWebTimeAsync(IPEndPoint server)
		{
			// NTP message size - 16 bytes of the digest (RFC 2030)
			var ntpData = new byte[48];
			// Setting the Leap Indicator, Version Number and Mode values
			ntpData[0] = 0x1B; // LI = 0 (no warning), VN = 3 (IPv4 only), Mode = 3 (Client Mode)

			using var udp = new UdpClient();
			udp.Connect(server);
			udp.Client.ReceiveTimeout = (int)TimeSpan.FromSeconds(1).TotalMilliseconds;
			await udp.SendAsync(ntpData, ntpData.Length);
			ntpData = udp.Receive(ref server);

			const byte serverReplyTime = 40;
			var integer = BinaryPrimitives.ReadUInt32BigEndian(ntpData.AsSpan(serverReplyTime));
			var fraction = BinaryPrimitives.ReadUInt32BigEndian(ntpData.AsSpan(serverReplyTime + 4));
			var milliseconds = integer * 1000UL + fraction * 1000UL / 0x100000000UL;

			return BaseTime.AddMilliseconds(milliseconds);
		}

		public static async ValueTask<DateTime> GetCurrentTime()
		{
			var ipS = await Dns.GetHostAddressesAsync(@"cn.ntp.org.cn");
			foreach (var ip in ipS)
			{
				try
				{
					return await GetWebTimeAsync(new IPEndPoint(ip, 123));
				}
				catch
				{
					// ignored
				}
			}
			return DateTime.UtcNow;
		}
	}
}
