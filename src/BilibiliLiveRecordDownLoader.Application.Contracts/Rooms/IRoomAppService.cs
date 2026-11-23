namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

public interface IRoomAppService
{
	long Version { get; }

	bool IsRecording { get; }

	IReadOnlyList<RoomSnapshot> GetRooms();

	Task<RoomSnapshot> AddAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default);

	Task<IReadOnlyList<RoomAddResult>> AddRangeAsync(IReadOnlyList<RoomSettingsDto> settings, CancellationToken cancellationToken = default);

	IAsyncEnumerable<FollowedRoomDto> GetFollowedRoomsAsync(CancellationToken cancellationToken = default);

	Task UpdateAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default);

	// 房间保持相对顺序移到 before 之前，before 为 null 时移到末尾
	// before 也在移动时以它之后第一个不移动的房间为准；before 不存在时不移动，未知的房间号忽略
	Task MoveAsync(IReadOnlyCollection<long> roomIds, long? before, CancellationToken cancellationToken = default);

	Task RemoveAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default);

	Task RefreshAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default);

	// 目录不一定已经创建
	Task<string> GetDirectoryAsync(long roomId, CancellationToken cancellationToken = default);
}
