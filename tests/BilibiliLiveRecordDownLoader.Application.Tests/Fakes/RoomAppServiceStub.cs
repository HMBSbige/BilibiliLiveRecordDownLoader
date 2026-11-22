using System.Runtime.CompilerServices;

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

	public virtual async IAsyncEnumerable<FollowedRoomDto> GetFollowedRoomsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await Task.CompletedTask;
		yield break;
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
}
