using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BilibiliLive.Api;

// 续期 Cookie 要先打开 correspond 页面取 refresh_csrf，页面路径是加密的时间戳
internal static partial class CorrespondPage
{
	public const string PublicKey = """
		-----BEGIN PUBLIC KEY-----
		MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDLgd2OAkcGVtoE3ThUREbio0Eg
		Uc/prcajMKXvkCKFCWhJYJcLkcM2DKKcSeFpD/j6Boy538YXnR6VhcuUJOhH2x71
		nzPjfdTcqMz7djHum0qSZA0AyCBDABUqCrfNgCiJ00Ra7GmRj+YCK1NJEuewlb40
		JNrRuoEUXpabUzGB8QIDAQAB
		-----END PUBLIC KEY-----
		""";

	[GeneratedRegex("""<div id="1-name">\s*([^<\s]+)\s*</div>""")]
	private static partial Regex RefreshCsrfPattern { get; }

	/// <param name="timestamp">cookie/info 返回的服务器时间</param>
	public static string CreatePath(long timestamp, string publicKey)
	{
		using RSA rsa = RSA.Create();
		rsa.ImportFromPem(publicKey);
		byte[] encrypted = rsa.Encrypt(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"refresh_{timestamp}")), RSAEncryptionPadding.OaepSHA256);
		return Convert.ToHexStringLower(encrypted);
	}

	public static string? ReadRefreshCsrf(string html)
	{
		return RefreshCsrfPattern.Match(html) is { Success: true } match ? match.Groups[1].Value : null;
	}
}
