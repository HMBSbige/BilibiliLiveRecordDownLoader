namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

public interface IRoomAppService
{
	long Version { get; }

	bool IsRecording { get; }

	IReadOnlyList<RoomSnapshot> GetRooms();

	Task<RoomSnapshot> AddAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default);

	// 已存在的房间跳过，单个房间失败不影响其他房间；结果与输入一一对应
	Task<IReadOnlyList<RoomAddResult>> AddRangeAsync(IReadOnlyList<RoomSettingsDto> settings, CancellationToken cancellationToken = default);

	IAsyncEnumerable<FollowedRoomDto> GetFollowedRoomsAsync(CancellationToken cancellationToken = default);

	Task UpdateAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default);

	// 按房间号而不是位置表达，期间增删房间也不会移错；房间保持原有的相对顺序移到 before 之前，before 为 null 时移到末尾
	// before 本身也在移动时以它之后第一个不移动的房间为准，before 不存在时不移动，未知的房间号忽略
	Task MoveAsync(IReadOnlyCollection<long> roomIds, long? before, CancellationToken cancellationToken = default);

	Task RemoveAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default);

	Task RefreshAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default);
}
