using BilibiliLive.Wbi;

namespace BilibiliLive.Tests.Wbi;

public class WbiSignerTest
{
	internal const string ImgKey = "7cd084941338484aae1ad9425b84077c";
	internal const string SubKey = "4932caff0ff746eab6f01bf08b70ac45";

	internal static string MixinKey(string imgKey, string subKey)
	{
		return WbiSigner.TryGetMixinKey(imgKey, subKey) ?? throw new InvalidOperationException("The test keys are too short.");
	}

	[Test]
	public async Task SignMatchesKnownVectorAndReplacesStaleSignatureAsync()
	{
		// Wbi 签名文档里的例子，另加上次签名留下的 wts 和 w_rid，它们不能参与签名
		KeyValuePair<string, string>[] parameters = [new("foo", "114"), new("bar", "514"), new("zab", "1919810"), new("wts", "0"), new("w_rid", "stale")];

		string query = WbiSigner.Sign(parameters, MixinKey(ImgKey, SubKey), 1702204169);

		await Assert.That(query).IsEqualTo("bar=514&foo=114&wts=1702204169&zab=1919810&w_rid=8f6f2b5b3d485fe1886cec6a0be8c5d4");
	}

	[Test]
	public async Task SignStripsReservedCharsAndEncodesLikeEncodeUriComponentAsync()
	{
		KeyValuePair<string, string>[] parameters = [new("q", "a b!'()*&c=中")];

		string query = WbiSigner.Sign(parameters, MixinKey(ImgKey, SubKey), 1);

		await Assert.That(query).StartsWith("q=a%20b%26c%3D%E4%B8%AD&wts=1&w_rid=");
	}
}
