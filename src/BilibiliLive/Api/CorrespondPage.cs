using CryptoBase.Hashes.Sha2;
using CryptoBase.Rsa;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace BilibiliLive.Api;

// 续期 Cookie 要先打开 correspond 页面取 refresh_csrf，页面路径是加密的时间戳
internal static partial class CorrespondPage
{
	public static readonly RsaKey PublicKey = RsaKey.ImportSubjectPublicKeyInfo
	(
		Convert.FromBase64String
		(
			"""
			MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDLgd2OAkcGVtoE3ThUREbio0Eg
			Uc/prcajMKXvkCKFCWhJYJcLkcM2DKKcSeFpD/j6Boy538YXnR6VhcuUJOhH2x71
			nzPjfdTcqMz7djHum0qSZA0AyCBDABUqCrfNgCiJ00Ra7GmRj+YCK1NJEuewlb40
			JNrRuoEUXpabUzGB8QIDAQAB
			"""
		)
	);

	[GeneratedRegex("""<div id="1-name">\s*([^<\s]+)\s*</div>""")]
	private static partial Regex RefreshCsrfPattern { get; }

	/// <param name="timestamp">cookie/info 返回的服务器时间</param>
	public static string CreatePath(long timestamp, RsaKey publicKey)
	{
		Span<byte> buffer = stackalloc byte[publicKey.ModulusLength];

		if (!Utf8.TryWrite(buffer, CultureInfo.InvariantCulture, $"refresh_{timestamp}", out int length))
		{
			throw new UnreachableException();
		}

		publicKey.EncryptOaep<Sha256Core>(buffer.Slice(0, length), buffer);
		return Convert.ToHexStringLower(buffer);
	}

	public static string? ReadRefreshCsrf(string html)
	{
		return RefreshCsrfPattern.Match(html) is { Success: true } match ? match.Groups[1].Value : null;
	}
}
