namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

// 录到的画质低于这次能录的最高画质，Highest 是接口所列画质中不高于所选画质的最高一档
public closed record QualityRestriction(long Highest)
{
	// 同一次接口返回里另一种编码的画质更高，它在编码优先级里排在录到的编码之后
	public sealed record OtherCodec(long Highest, StreamCodec Codec, long Quality) : QualityRestriction(Highest);

	// 画质更高的编码排在前面，已经先试过但所有线路都没连上，调整编码优先级也没用
	public sealed record OtherCodecUnavailable(long Highest, StreamCodec Codec, long Quality) : QualityRestriction(Highest);

	// 请求候选流时本地没有配 Cookie
	public sealed record NotLoggedIn(long Highest) : QualityRestriction(Highest);

	// 配了 Cookie 仍然受限；已登录的账号也可能被限，所以不能断言没登录
	public sealed record LoginMayHaveExpired(long Highest) : QualityRestriction(Highest);
}
