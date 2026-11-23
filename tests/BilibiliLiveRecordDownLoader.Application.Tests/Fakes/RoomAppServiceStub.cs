using System.Globalization;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal abstract class RoomAppServiceStub : IRoomAppService
{
	public virtual long Version => 0;

	public virtual bool IsRecording => false;

	public virtual IReadOnlyList<RoomSnapshot> GetRooms()
	{
		return [];
	}

	public virtual Task<RoomSnapshot> AddAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public virtual Task<IReadOnlyList<RoomAddResult>> AddRangeAsync(IReadOnlyList<RoomSettingsDto> settings, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public virtual IAsyncEnumerable<FollowedRoomDto> GetFollowedRoomsAsync(CancellationToken cancellationToken = default)
	{
		return AsyncEnumerable.Empty<FollowedRoomDto>();
	}

	public virtual Task UpdateAsync(RoomSettingsDto settings, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public virtual Task MoveAsync(IReadOnlyCollection<long> roomIds, long? before, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public virtual Task RemoveAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public virtual Task RefreshAsync(IReadOnlyCollection<long> roomIds, CancellationToken cancellationToken = default)
	{
		throw new NotSupportedException();
	}

	public virtual Task<string> GetDirectoryAsync(long roomId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(Path.Combine("rooms", roomId.ToString(CultureInfo.InvariantCulture)));
	}
}
