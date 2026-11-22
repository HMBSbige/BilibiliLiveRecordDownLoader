using System.Diagnostics;

namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

public enum DanmakuEntryKind
{
	Comment,
	SuperChat,
	Gift,
	Guard,
	// 标题变化、连接断开和连接建立，只出现在弹幕文件里
	Notice
}

// 文字在读取时按当前语言生成，切换语言后滚动到的行就是新语言，不必整表重建
public sealed partial class DanmakuEntryViewModel : ReactiveObject
{
	private readonly DanmakuEntryDto _entry;

	private DanmakuEntryViewModel(long sequence, TimeSpan? time, string timeText, DanmakuEntryDto entry)
	{
		Sequence = sequence;
		Time = time;
		TimeText = timeText;
		_entry = entry;
		Kind = entry switch
		{
			DanmakuEntryDto.Comment => DanmakuEntryKind.Comment,
			DanmakuEntryDto.SuperChat => DanmakuEntryKind.SuperChat,
			DanmakuEntryDto.Gift => DanmakuEntryKind.Gift,
			DanmakuEntryDto.Guard => DanmakuEntryKind.Guard,
			DanmakuEntryDto.TitleChange or DanmakuEntryDto.Gap or DanmakuEntryDto.Connection => DanmakuEntryKind.Notice,
			DanmakuEntryDto.SuperChatDeletion => throw new ArgumentException(@"A deletion marks the deleted super chats instead of being listed, see MarkDeleted.", nameof(entry))
		};

		if (entry is DanmakuEntryDto.SuperChat superChat)
		{
			SuperChatTier tier = SuperChatTier.For(superChat.Price);
			(SuperChatBackground, SuperChatPriceColor, SuperChatBottomBackground) = (tier.Background, tier.PriceColor, tier.BottomBackground);
		}
	}

	public static DanmakuEntryViewModel FromLive(LiveDanmakuEntryDto entry)
	{
		return new DanmakuEntryViewModel(entry.Sequence, null, entry.ReceivedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), entry.Entry);
	}

	// 文件里的条目没有序号，用在文件中的位置代替
	public static DanmakuEntryViewModel FromFile(long index, DanmakuFileEntryDto entry)
	{
		return new DanmakuEntryViewModel(index, entry.Time, FormatTime(entry.Time), entry.Entry);
	}

	public long Sequence { get; }

	// 文件内时间；实时弹幕为 null
	public TimeSpan? Time { get; }

	public string TimeText { get; }

	public DanmakuEntryKind Kind { get; }

	public bool IsSuperChat => Kind is DanmakuEntryKind.SuperChat;

	// 直播间删掉的 SC 留在列表里可查，只是标出来；删除可能在 SC 显示之后才到
	[Reactive]
	public partial bool IsDeleted { get; private set; }

	public string DeletedText => AppLanguage.Current.DanmakuSuperChatDeleted;

	public bool IsNotice => Kind is DanmakuEntryKind.Notice;

	// 节奏风暴是系统代发的，淡化显示
	public bool IsStorm => _entry is DanmakuEntryDto.Comment { IsStorm: true };

	public string? Name => Sender?.Name;

	// 弹幕和 SC 是昵称说了什么，礼物和上舰是昵称做了什么
	public string? NameSeparator => Kind switch
	{
		DanmakuEntryKind.Comment or DanmakuEntryKind.SuperChat => AppLanguage.Current.DanmakuNameSeparator,
		DanmakuEntryKind.Gift or DanmakuEntryKind.Guard => " ",
		_ => null
	};

	// 舰队等级优先，其次是粉丝牌
	public string? Badge => Sender switch
	{
		{ GuardLevel: not GuardLevel.None and var level } => FormatGuardLevel(level),
		{ Medal: { } medal } => string.Create(CultureInfo.CurrentCulture, $"{medal.Name} {medal.Level}"),
		_ => null
	};

	public bool IsGuardBadge => Sender is { GuardLevel: not GuardLevel.None };

	// 点亮的粉丝牌用它自己的颜色，其余用主题色
	public int? MedalColor => Sender is { GuardLevel: GuardLevel.None, Medal: { IsLit: true, Color: var color } } ? color : null;

	public string Text => _entry switch
	{
		DanmakuEntryDto.Comment comment => comment.Text,
		DanmakuEntryDto.SuperChat superChat => superChat.Text,
		DanmakuEntryDto.Gift gift => AppLanguage.Current.DanmakuGiftText.ToString(gift.Name, gift.Count.ToString(CultureInfo.CurrentCulture)),
		DanmakuEntryDto.Guard { Months: > 1, IsRenewal: true } guard => AppLanguage.Current.DanmakuGuardRenewalMonthsText.ToString(FormatGuardLevel(guard.Level), guard.Months.ToString(CultureInfo.CurrentCulture)),
		DanmakuEntryDto.Guard { Months: > 1 } guard => AppLanguage.Current.DanmakuGuardMonthsText.ToString(FormatGuardLevel(guard.Level), guard.Months.ToString(CultureInfo.CurrentCulture)),
		DanmakuEntryDto.Guard { IsRenewal: true } guard => AppLanguage.Current.DanmakuGuardRenewalText.ToString(FormatGuardLevel(guard.Level)),
		DanmakuEntryDto.Guard guard => AppLanguage.Current.DanmakuGuardText.ToString(FormatGuardLevel(guard.Level)),
		DanmakuEntryDto.TitleChange title => AppLanguage.Current.DanmakuTitleText.ToString(title.Title),
		DanmakuEntryDto.Gap gap => AppLanguage.Current.DanmakuGapText.ToString(FormatTime(gap.End)),
		DanmakuEntryDto.Connection connection => AppLanguage.Current.DanmakuConnectionText.ToString(DescribeIdentity(!connection.IsAnonymous, connection.IsMasked)),
		DanmakuEntryDto.SuperChatDeletion => throw new UnreachableException()
	};

	// 银瓜子礼物不花钱，不显示价格
	public string? PriceText => _entry switch
	{
		DanmakuEntryDto.SuperChat superChat => FormatPrice(superChat.Price),
		DanmakuEntryDto.Gift { Price: > 0 } gift => FormatPrice(gift.Price),
		DanmakuEntryDto.Guard { Price: > 0 } guard => FormatPrice(guard.Price),
		_ => null
	};

	// SC 的配色和字幕共用一份，RGB；不是 SC 时为 0
	public int SuperChatBackground { get; }

	public int SuperChatPriceColor { get; }

	public int SuperChatBottomBackground { get; }

	private DanmuSender? Sender => _entry switch
	{
		DanmakuEntryDto.Comment comment => comment.Sender,
		DanmakuEntryDto.SuperChat superChat => superChat.Sender,
		DanmakuEntryDto.Gift gift => gift.Sender,
		DanmakuEntryDto.Guard guard => guard.Sender,
		DanmakuEntryDto.TitleChange or DanmakuEntryDto.Gap or DanmakuEntryDto.Connection => null,
		DanmakuEntryDto.SuperChatDeletion => throw new UnreachableException()
	};

	// id 为 0 的 SC 是文件里没记 id，不能和任何删除对上
	public static void MarkDeleted(IEnumerable<DanmakuEntryViewModel> entries, IReadOnlyCollection<long> ids)
	{
		foreach (DanmakuEntryViewModel entry in entries)
		{
			if (entry._entry is DanmakuEntryDto.SuperChat { Id: not 0 and var id } && ids.Contains(id))
			{
				entry.IsDeleted = true;
			}
		}
	}

	public bool Contains(string text)
	{
		return Text.Contains(text, StringComparison.CurrentCultureIgnoreCase) || (Name?.Contains(text, StringComparison.CurrentCultureIgnoreCase) ?? false);
	}

	public static string FormatTime(TimeSpan time)
	{
		time = time > TimeSpan.Zero ? time : TimeSpan.Zero;
		return string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time:mm\\:ss}");
	}

	// userId 为 null 表示还没连上过
	public static string? FormatIdentity(long? userId, bool isMasked)
	{
		return userId switch
		{
			null => null,
			> 0 => AppLanguage.Current.DanmakuLoggedIn,
			_ => DescribeIdentity(false, isMasked)
		};
	}

	private static string DescribeIdentity(bool isLoggedIn, bool isMasked)
	{
		return isLoggedIn ? AppLanguage.Current.DanmakuLoggedIn : isMasked ? AppLanguage.Current.DanmakuAnonymousMasked : AppLanguage.Current.DanmakuAnonymous;
	}

	// 价格是千分之一元，整数元不带小数
	public static string FormatPrice(long price)
	{
		return AppLanguage.Current.DanmakuPrice.ToString((price / 1000m).ToString("0.##", CultureInfo.CurrentCulture));
	}

	public static string FormatGuardLevel(GuardLevel level)
	{
		return level switch
		{
			GuardLevel.Governor => AppLanguage.Current.GuardLevelGovernor,
			GuardLevel.Admiral => AppLanguage.Current.GuardLevelAdmiral,
			_ => AppLanguage.Current.GuardLevelCaptain
		};
	}
}
