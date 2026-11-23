using System.Diagnostics;

namespace BilibiliLiveRecordDownLoader.ViewModels.Danmaku;

public enum DanmakuEntryKind
{
	Comment,
	SuperChat,
	Gift,
	Guard,
	Notice
}

// 文字读取时才按当前语言生成，切换语言不必重建列表
public sealed partial class DanmakuEntryViewModel : ReactiveObject
{
	private readonly DanmakuEntryDto _entry;
	private readonly SuperChatTier _tier;

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
		_tier = entry is DanmakuEntryDto.SuperChat superChat ? SuperChatTier.For(superChat.Price) : default;
	}

	public static DanmakuEntryViewModel FromLive(LiveDanmakuEntryDto entry)
	{
		return new DanmakuEntryViewModel(entry.Sequence, null, entry.ReceivedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), entry.Entry);
	}

	public static DanmakuEntryViewModel FromFile(DanmakuFileEntryDto entry)
	{
		return new DanmakuEntryViewModel(0, entry.Time, FormatTime(entry.Time), entry.Entry);
	}

	// 实时弹幕的推送序号，文件里的条目为 0
	public long Sequence { get; }

	// 实时弹幕为 null
	public TimeSpan? Time { get; }

	public string TimeText { get; }

	public DanmakuEntryKind Kind { get; }

	public bool IsSuperChat => Kind is DanmakuEntryKind.SuperChat;

	// 删除可能在 SC 显示之后才到
	[Reactive]
	public partial bool IsDeleted { get; private set; }

	public bool IsNotice => Kind is DanmakuEntryKind.Notice;

	public bool IsStorm => _entry is DanmakuEntryDto.Comment { IsStorm: true };

	public string? Name => Sender?.Name;

	// 礼物和上舰的文字和昵称连成一句
	public string? NameSeparator => Kind switch
	{
		DanmakuEntryKind.Comment or DanmakuEntryKind.SuperChat => AppLanguage.Current.DanmakuNameSeparator,
		DanmakuEntryKind.Gift or DanmakuEntryKind.Guard => " ",
		DanmakuEntryKind.Notice => null
	};

	public string? Badge => Sender switch
	{
		{ GuardLevel: not GuardLevel.None and var level } => DanmakuGuardText.FormatLevel(level),
		{ Medal: { } medal } => string.Create(CultureInfo.CurrentCulture, $"{medal.Name} {medal.Level}"),
		_ => null
	};

	public bool IsGuardBadge => Sender is { GuardLevel: not GuardLevel.None };

	// null 时用主题色
	public int? MedalColor => Sender is { GuardLevel: GuardLevel.None, Medal: { IsLit: true, Color: var color } } ? color : null;

	// 没点亮的粉丝牌只画边框，灰底白字看不清
	public bool IsUnlitMedal => Sender is { GuardLevel: GuardLevel.None, Medal.IsLit: false };

	public string Text => _entry switch
	{
		DanmakuEntryDto.Comment comment => comment.Text,
		DanmakuEntryDto.SuperChat superChat => superChat.Text,
		DanmakuEntryDto.Gift gift => AppLanguage.Current.DanmakuGiftText.ToString(gift.Name, gift.Count.ToString(CultureInfo.CurrentCulture)),
		DanmakuEntryDto.Guard guard => DanmakuGuardText.Format(guard.Level, guard.Months, guard.IsRenewal),
		DanmakuEntryDto.TitleChange title => AppLanguage.Current.DanmakuTitleText.ToString(title.Title),
		DanmakuEntryDto.Gap gap => AppLanguage.Current.DanmakuGapText.ToString(FormatTime(gap.End)),
		DanmakuEntryDto.Connection connection => AppLanguage.Current.DanmakuConnectionText.ToString(DescribeIdentity(connection)),
		DanmakuEntryDto.SuperChatDeletion => throw new UnreachableException()
	};

	public string? PriceText => _entry switch
	{
		DanmakuEntryDto.SuperChat superChat => FormatPrice(superChat.Price),
		DanmakuEntryDto.Gift { Price: > 0 } gift => FormatPrice(gift.Price),
		DanmakuEntryDto.Guard { Price: > 0 } guard => FormatPrice(guard.Price),
		DanmakuEntryDto.Comment or DanmakuEntryDto.Gift or DanmakuEntryDto.Guard or DanmakuEntryDto.TitleChange or DanmakuEntryDto.Gap or DanmakuEntryDto.Connection => null,
		DanmakuEntryDto.SuperChatDeletion => throw new UnreachableException()
	};

	// 列表项的内容是视图模型，读屏要单独给出整行的文字
	public string AccessibleText => string.Join(' ', ((string?[])[TimeText, Badge, Name is null ? Text : $"{Name}{NameSeparator}{Text}", PriceText, IsDeleted ? AppLanguage.Current.DanmakuSuperChatDeleted : null]).Where(static part => !string.IsNullOrEmpty(part)));

	// RGB；不是 SC 时为 0
	public int SuperChatBackground => _tier.Background;

	public int SuperChatPriceColor => _tier.PriceColor;

	public int SuperChatBottomBackground => _tier.BottomBackground;

	public int SuperChatTextColor => _tier.TextColor;

	private DanmuSender? Sender => _entry switch
	{
		DanmakuEntryDto.Comment comment => comment.Sender,
		DanmakuEntryDto.SuperChat superChat => superChat.Sender,
		DanmakuEntryDto.Gift gift => gift.Sender,
		DanmakuEntryDto.Guard guard => guard.Sender,
		DanmakuEntryDto.TitleChange or DanmakuEntryDto.Gap or DanmakuEntryDto.Connection => null,
		DanmakuEntryDto.SuperChatDeletion => throw new UnreachableException()
	};

	// id 为 0 是文件里没记 id
	public static void MarkDeleted(IEnumerable<DanmakuEntryViewModel> entries, IReadOnlyCollection<long> ids)
	{
		foreach (DanmakuEntryViewModel entry in entries)
		{
			if (entry._entry is DanmakuEntryDto.SuperChat { Id: not 0 and var id } && ids.Contains(id))
			{
				entry.IsDeleted = true;
				entry.RaisePropertyChanged(nameof(AccessibleText));
			}
		}
	}

	// 过滤在界面线程上逐条比较，按区域性比较中文会慢很多
	public bool Contains(string text)
	{
		return Text.Contains(text, StringComparison.OrdinalIgnoreCase) || (Name?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
	}

	public static string FormatTime(TimeSpan time)
	{
		time = time > TimeSpan.Zero ? time : TimeSpan.Zero;
		return string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time:mm\\:ss}");
	}

	// 价格单位是千分之一元
	public static string FormatPrice(long price)
	{
		return AppLanguage.Current.DanmakuPrice.ToString((price / 1000m).ToString("0.##", CultureInfo.CurrentCulture));
	}

	private static string DescribeIdentity(DanmakuEntryDto.Connection connection)
	{
		return connection switch
		{
			{ IsAnonymous: false } => AppLanguage.Current.DanmakuLoggedIn,
			{ IsMasked: true } => AppLanguage.Current.DanmakuAnonymousMasked,
			_ => AppLanguage.Current.DanmakuAnonymous
		};
	}
}
