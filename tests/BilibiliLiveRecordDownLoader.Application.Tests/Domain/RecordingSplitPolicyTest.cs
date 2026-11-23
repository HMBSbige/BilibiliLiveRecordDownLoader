namespace BilibiliLiveRecordDownLoader.Application.Tests.Domain;

public sealed class RecordingSplitPolicyTest
{
	public static IEnumerable<Func<(RecordingSplitMode Mode, TimeSpan? Duration, long? SizeMegabytes, RecordingSplit Expected)>> RoomSplitOverrides()
	{
		yield return static () => (RecordingSplitMode.None, null, null, new RecordingSplit.None());
		yield return static () => (RecordingSplitMode.Duration, TimeSpan.FromMinutes(20), null, new RecordingSplit.ByDuration(TimeSpan.FromMinutes(20)));
		yield return static () => (RecordingSplitMode.Duration, null, null, new RecordingSplit.ByDuration(TimeSpan.FromMinutes(45)));
		yield return static () => (RecordingSplitMode.Size, null, 100, new RecordingSplit.BySize(100L << 20));
		yield return static () => (RecordingSplitMode.Size, null, null, new RecordingSplit.BySize(2000L << 20));
	}

	[Test]
	public async Task RoomFollowingTheGlobalSplitIgnoresItsLeftoverValuesAsync()
	{
		AppConfig global = new() { SplitMode = RecordingSplitMode.Duration, SplitDuration = TimeSpan.FromMinutes(45) };

		RecordingSplitPolicy policy = RecordingSplitPolicy.Create(global, new RoomConfig { RoomId = 1, SplitDuration = TimeSpan.FromMinutes(5) });

		await Assert.That(policy.Split).IsEqualTo(new RecordingSplit.ByDuration(TimeSpan.FromMinutes(45)));
	}

	[Test]
	[MethodDataSource(nameof(RoomSplitOverrides))]
	public async Task RoomSplitOverrideTakesMissingValuesFromTheGlobalSettingsAsync(RecordingSplitMode mode, TimeSpan? duration, long? sizeMegabytes, RecordingSplit expected)
	{
		AppConfig global = new() { SplitMode = RecordingSplitMode.Size, SplitDuration = TimeSpan.FromMinutes(45), SplitSizeMegabytes = 2000 };
		RoomConfig room = new() { RoomId = 1, SplitMode = mode, SplitDuration = duration, SplitSizeMegabytes = sizeMegabytes };

		await Assert.That(RecordingSplitPolicy.Create(global, room).Split).IsEqualTo(expected);
	}

	[Test]
	[Arguments(false, null, false)]
	[Arguments(true, null, true)]
	[Arguments(true, false, false)]
	[Arguments(false, true, true)]
	public async Task RoomTitleSplitOverrideWinsOverTheGlobalSwitchAsync(bool global, bool? room, bool expected)
	{
		RecordingSplitPolicy policy = RecordingSplitPolicy.Create(new AppConfig { SplitOnTitleChange = global }, new RoomConfig { RoomId = 1, SplitOnTitleChange = room });

		await Assert.That(policy.OnTitleChange).IsEqualTo(expected);
	}
}
