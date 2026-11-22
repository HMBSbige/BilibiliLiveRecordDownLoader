namespace BilibiliLiveRecordDownLoader.Application.Tests.Domain;

public sealed class RecordingSplitPolicyTest
{
	[Test]
	public async Task RoomFollowingTheGlobalSplitIgnoresItsLeftoverValuesAsync()
	{
		AppConfig global = new() { SplitMode = RecordingSplitMode.Duration, SplitDuration = TimeSpan.FromMinutes(45) };

		RecordingSplitPolicy policy = RecordingSplitPolicy.Create(global, new RoomConfig { RoomId = 1, SplitDuration = TimeSpan.FromMinutes(5) });

		await Assert.That(policy.Split).IsEqualTo(new RecordingSplit.ByDuration(TimeSpan.FromMinutes(45)));
	}

	[Test]
	[Arguments(RecordingSplitMode.None, null, null, "None")]
	[Arguments(RecordingSplitMode.Duration, 20, null, "ByDuration:00:20:00")]
	[Arguments(RecordingSplitMode.Duration, null, null, "ByDuration:00:45:00")]
	[Arguments(RecordingSplitMode.Size, null, 100L, "BySize:104857600")]
	[Arguments(RecordingSplitMode.Size, null, null, "BySize:2097152000")]
	public async Task RoomSplitOverrideTakesMissingValuesFromTheGlobalSettingsAsync(RecordingSplitMode mode, int? minutes, long? megabytes, string expected)
	{
		AppConfig global = new() { SplitMode = RecordingSplitMode.Size, SplitDuration = TimeSpan.FromMinutes(45), SplitSizeMegabytes = 2000 };
		RoomConfig room = new() { RoomId = 1, SplitMode = mode, SplitDuration = minutes is { } value ? TimeSpan.FromMinutes(value) : null, SplitSizeMegabytes = megabytes };

		string actual = RecordingSplitPolicy.Create(global, room).Split switch
		{
			RecordingSplit.None => "None",
			RecordingSplit.ByDuration duration => $"ByDuration:{duration.Duration}",
			RecordingSplit.BySize size => $"BySize:{size.MaxBytes}"
		};

		await Assert.That(actual).IsEqualTo(expected);
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
