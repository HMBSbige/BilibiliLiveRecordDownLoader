using BilibiliLiveRecordDownLoader.Application.Contracts.Storage;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Platform;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class GeneralSettingsViewModelTest
{
	[Test]
	[Timeout(30_000)]
	[Arguments("  ", null)]
	[Arguments("  records  ", "records")]
	public async Task RecordDirectoryShowsTheDirectoryThatIsSavedAsync(string typed, string? expected, CancellationToken cancellationToken)
	{
		AppConfig config = AppConfig.CreateDefault();
		config.RecordDirectory = "old";
		FakeConfigManager configManager = new(config);
		using GeneralSettingsViewModel viewModel = new(configManager, new NoDiskSpace(), new UnsupportedAutoStart());
		viewModel.ApplyConfig(config);
		string saved = expected ?? ConfigurationConsts.DefaultRecordDirectory;

		viewModel.RecordDirectory = typed;

		await TestWait.UntilAsync(() => config.RecordDirectory == saved, "the record directory is saved", cancellationToken);

		await Assert.That(viewModel.RecordDirectory).IsEqualTo(saved);
	}

	private sealed class NoDiskSpace : IDiskSpaceService
	{
		public DiskSpaceInfo? GetDiskSpace(string path)
		{
			return null;
		}
	}

	private sealed class UnsupportedAutoStart : IAutoStartService
	{
		public bool IsSupported => false;

		public bool IsEnabled()
		{
			return false;
		}

		public void SetEnabled(bool enabled)
		{
			throw new NotSupportedException();
		}

		public void TakeOverExisting()
		{
		}
	}
}
