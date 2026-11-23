namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

// Highest 是接口所列画质中不高于所选画质的最高一档
public closed record QualityRestriction(long Highest)
{
	// 同一次接口返回里另一种编码的画质更高，但它在编码优先级里排在录到的编码之后
	public sealed record OtherCodec(long Highest, StreamCodec Codec, long Quality) : QualityRestriction(Highest);

	// 画质更高的编码优先级在前，已经试过但所有线路都没连上
	public sealed record OtherCodecUnavailable(long Highest, StreamCodec Codec, long Quality) : QualityRestriction(Highest);

	// 请求候选流时本地没有配 Cookie
	public sealed record NotLoggedIn(long Highest) : QualityRestriction(Highest);

	// 配了 Cookie 仍然受限；已登录的账号也可能被限，不能断言没登录
	public sealed record LoginMayHaveExpired(long Highest) : QualityRestriction(Highest);
}
