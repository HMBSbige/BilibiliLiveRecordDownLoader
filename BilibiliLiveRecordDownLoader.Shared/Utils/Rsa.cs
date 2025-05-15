using System.Security.Cryptography;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Shared.Utils;

public static class Rsa
{
	private static RSA ReadKey(string pemContents)
	{
		const string header = @"-----BEGIN PUBLIC KEY-----";
		const string footer = @"-----END PUBLIC KEY-----";

		if (pemContents.StartsWith(header))
		{
			int endIdx = pemContents.IndexOf(footer, header.Length, StringComparison.Ordinal);
			string base64 = new(pemContents.AsSpan(header.Length, endIdx - header.Length));

			byte[] der = Convert.FromBase64String(base64);
			RSA rsa = RSA.Create();
			rsa.ImportSubjectPublicKeyInfo(der, out _);
			return rsa;
		}

		// "BEGIN PRIVATE KEY" (ImportPkcs8PrivateKey),
		// "BEGIN ENCRYPTED PRIVATE KEY" (ImportEncryptedPkcs8PrivateKey),
		// "BEGIN PUBLIC KEY" (ImportSubjectPublicKeyInfo),
		// "BEGIN RSA PUBLIC KEY" (ImportRSAPublicKey)
		throw new NotImplementedException();
	}

	public static string Encrypt(string publicKey, string str)
	{
		using RSA rsa = ReadKey(publicKey);
		byte[] cipherBytes = rsa.Encrypt(Encoding.UTF8.GetBytes(str), RSAEncryptionPadding.Pkcs1);
		return Convert.ToBase64String(cipherBytes);
	}
}
