using BilibiliLiveRecordDownLoader.Application.Contracts.Storage;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class GeneralSettingsViewModelTest
{
	[Test]
	[Arguments("  ", null)]
	[Arguments("  records  ", "records")]
	public async Task RecordDirectoryShowsTheDirectoryThatIsSavedAsync(string typed, string? expected, CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.RecordDirectory = "old";
		FakeConfigManager configManager = new(config);
		PlatformStub platform = new();
		using GeneralSettingsViewModel viewModel = new(new SettingsAppService(configManager), new NoDiskSpace(), new UnsupportedAutoStart(), platform, platform);
		viewModel.ApplySettings(config.ToSettingsDto());
		string saved = expected ?? ConfigurationConsts.DefaultRecordDirectory;

		viewModel.RecordDirectory = typed;

		await TestWait.UntilAsync(() => config.RecordDirectory == saved, "the record directory is saved", cancellationToken);

		await Assert.That(viewModel.RecordDirectory).IsEqualTo(saved);
	}

	public static IEnumerable<Func<(DiskSpaceInfo? DiskSpace, double UsagePercent, bool IsLow)>> DiskSpaceCases()
	{
		yield return static () => (null, 0, false);
		yield return static () => (new DiskSpaceInfo(4L << 30, 3L << 30), 25, false);
		yield return static () => (new DiskSpaceInfo(2L << 30, 1L << 29), 75, true);
	}

	// 先显示一块快满的磁盘，查不到磁盘信息时要清掉
	[Test]
	[MethodDataSource(nameof(DiskSpaceCases))]
	public async Task DiskSpaceShowsTheLatestReadingAsync(DiskSpaceInfo? diskSpace, double usagePercent, bool isLow)
	{
		PlatformStub platform = new();
		using GeneralSettingsViewModel viewModel = new(new SettingsAppService(new FakeConfigManager()), new NoDiskSpace(), new UnsupportedAutoStart(), platform, platform);
		viewModel.ApplyDiskSpace(new DiskSpaceInfo(8L << 30, 1L << 28));

		viewModel.ApplyDiskSpace(diskSpace);

		using (Assert.Multiple())
		{
			await Assert.That(viewModel.DiskSpaceText is not null).IsEqualTo(diskSpace is not null);
			await Assert.That(viewModel.DiskUsagePercent).IsEqualTo(usagePercent);
			await Assert.That(viewModel.IsDiskSpaceLow).IsEqualTo(isLow);
		}
	}

	private sealed class NoDiskSpace : IDiskSpaceService
	{
		public DiskSpaceInfo? GetDiskSpace(string path)
		{
			return null;
		}
	}
}
