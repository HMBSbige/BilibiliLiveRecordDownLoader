using BilibiliLiveRecordDownLoader.Domain.Shared.Recording;

namespace BilibiliLiveRecordDownLoader.Configuration;

// 已经删掉的设置：读配置时迁移到现在的设置，之后不再写出
internal sealed record RetiredSettings(bool RequestTrueOriginal)
{
	/// <returns>是否要写回配置，把已删掉的设置从文件里去掉</returns>
	public bool ApplyTo(AppConfig config)
	{
		// 4.0 的实验开关，打开时原画的房间请求原画真彩；现在原画真彩是单独的画质
		if (!RequestTrueOriginal)
		{
			return false;
		}

		// 手改的配置里 Rooms 或其中的元素可能是 null，留给 ApplyDefaults 清理
		// ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
		config.Rooms = [.. (config.Rooms ?? []).Select(static room => room is { } current && current.Quality == StreamQuality.Original ? current with { Quality = StreamQuality.OriginalTrueColor } : room)];
		return true;
	}
}
