using BilibiliLive.Danmu;
using BilibiliLiveRecordDownLoader.Application.Contracts.Danmaku;
using System.Collections.Concurrent;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed class FakeDanmakuAppService : IDanmakuAppService
{
	public ConcurrentQueue<FakeDanmakuFeed> Feeds { get; } = new();

	public HashSet<long> MissingRooms { get; } = [];

	public Func<string, CancellationToken, Task<DanmakuFileDto>> OpenFile { get; set; } = static (path, _) => throw new FileNotFoundException(null, path);

	public Func<string, TimeSpan, DanmakuOffsetResult> ChangeOffset { get; set; } = static (_, _) => throw new NotSupportedException();

	public Func<string, DanmakuHighlightDto, string> QueueHighlightExport { get; set; } = static (_, _) => throw new NotSupportedException();

	public IDanmakuFeed Subscribe(long roomId)
	{
		if (MissingRooms.Contains(roomId))
		{
			throw new KeyNotFoundException();
		}

		FakeDanmakuFeed feed = new(roomId);
		Feeds.Enqueue(feed);
		return feed;
	}

	public Task<DanmakuFileDto> OpenFileAsync(string path, CancellationToken cancellationToken = default)
	{
		return OpenFile(path, cancellationToken);
	}

	public Task<DanmakuOffsetResult> ChangeOffsetAsync(string path, TimeSpan offset, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(ChangeOffset(path, offset));
	}

	public Task<string> QueueHighlightExportAsync(string danmakuPath, DanmakuHighlightDto highlight, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(QueueHighlightExport(danmakuPath, highlight));
	}
}

// 和应用服务一样只保留最近的条目
internal sealed class FakeDanmakuFeed(long roomId) : IDanmakuFeed
{
	private readonly List<LiveDanmakuEntryDto> _entries = [];
	private long _sequence;

	public long RoomId { get; } = roomId;

	public long Version { get; private set; }

	public DanmakuFeedStatus Status { get; set; } = new(DanmakuConnectionState.Connected, 1, 0, true, 0, null);

	public bool IsDisposed { get; private set; }

	public IReadOnlyList<LiveDanmakuEntryDto> GetEntries(long afterSequence)
	{
		return [.. _entries.Where(entry => entry.Sequence > afterSequence)];
	}

	public void Add(params DanmakuEntryDto[] entries)
	{
		foreach (DanmakuEntryDto entry in entries)
		{
			_entries.Add(new LiveDanmakuEntryDto(++_sequence, DateTimeOffset.UnixEpoch.AddSeconds(_sequence), entry));
		}

		if (_entries.Count > DanmakuConsts.MaxFeedEntries)
		{
			_entries.RemoveRange(0, _entries.Count - DanmakuConsts.MaxFeedEntries);
		}

		++Version;
	}

	public void Dispose()
	{
		IsDisposed = true;
	}

	public static DanmakuEntryDto.Comment Comment(string text, string name = "观众")
	{
		return new DanmakuEntryDto.Comment(new DanmuSender(1, name, GuardLevel.None, null), text, 0xFFFFFF, false);
	}
}
