namespace BilibiliLiveRecordDownLoader.Domain.Shared.Recording;

public static class StreamQualityConsts
{
	// B 站返回的画质列表里没有这一档，用户不能直接选，所以不放进 StreamQuality；画质为原画时按实验性开关代为请求，拿不到时 B 站照常给原画
	public const long TrueOriginal = 25000;
}
