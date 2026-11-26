namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

// Highest 是接口所列画质中不超过所选画质的最高一档，都超过时是最低的一档
public closed record QualityRestriction(StreamQuality Highest)
{
	// 同一次接口返回里另一种编码的画质更高，先试过它但所有线路都没连上
	public sealed record OtherCodecUnavailable(StreamQuality Highest, StreamCodec Codec, StreamQuality Quality) : QualityRestriction(Highest);

	// 请求候选流时本地没有配 Cookie
	public sealed record NotLoggedIn(StreamQuality Highest) : QualityRestriction(Highest);

	// 配了 Cookie 仍然受限；已登录的账号也可能被限，不能断言没登录
	public sealed record LoginMayHaveExpired(StreamQuality Highest) : QualityRestriction(Highest);
}
