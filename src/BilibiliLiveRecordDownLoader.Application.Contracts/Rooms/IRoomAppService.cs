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

	Task RemoveAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default);

	Task RefreshAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default);
}
