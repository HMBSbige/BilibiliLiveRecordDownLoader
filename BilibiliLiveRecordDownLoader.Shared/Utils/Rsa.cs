using System;
using System.Security.Cryptography;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Shared.Utils
{
	public static class Rsa
	{
		private static RSA ReadKey(string pemContents)
		{
			const string header = @"-----BEGIN PUBLIC KEY-----";
			const string footer = @"-----END PUBLIC KEY-----";

			if (pemContents.StartsWith(header))
			{
				var endIdx = pemContents.IndexOf(footer, header.Length, StringComparison.Ordinal);
				var base64 = new string(pemContents.AsSpan(header.Length, endIdx - header.Length));

				var der = Convert.FromBase64String(base64);
				var rsa = RSA.Create();
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
			using var rsa = ReadKey(publicKey);
			var cipherBytes = rsa.Encrypt(Encoding.UTF8.GetBytes(str), RSAEncryptionPadding.Pkcs1);
			return Convert.ToBase64String(cipherBytes);
		}
	}
}
