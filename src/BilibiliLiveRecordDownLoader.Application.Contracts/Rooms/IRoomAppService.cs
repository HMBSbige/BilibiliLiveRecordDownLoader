namespace BilibiliLiveRecordDownLoader.Application.Contracts.Rooms;

public interface IRoomAppService
{
	long Version { get; }

	bool IsRecording { get; }

	IReadOnlyList<RoomSnapshot> GetRooms();

	Task<RoomSnapshot> AddAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default);

	Task UpdateAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default);

	Task RemoveAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default);

	Task RefreshAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default);
}
