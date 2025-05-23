using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.Views.Dialogs;
using DynamicData;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Threading;
using Microsoft.Win32;
using ModernWpf.Controls;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using RunAtStartup;
using System.Reactive;
using System.Reactive.Linq;
using UpdateChecker;

namespace BilibiliLiveRecordDownLoader.ViewModels;

public class SettingViewModel : ReactiveObject, IRoutableViewModel
{
	public string UrlPathSegment => @"Settings";

	public IScreen HostScreen { get; }

	#region 属性

	[Reactive]
	public string? DiskUsageProgressBarText { get; set; }

	[Reactive]
	public double DiskUsageProgressBarValue { get; set; }

	[Reactive]
	public string? UpdateStatus { get; set; }

	[Reactive]
	public bool IsRunOnStartup { get; set; }

	#endregion

	#region Command

	public ReactiveCommand<Unit, Unit> SelectMainDirCommand { get; }
	public ReactiveCommand<Unit, Unit> OpenMainDirCommand { get; }
	public ReactiveCommand<Unit, Unit> CheckUpdateCommand { get; }

	#endregion

	private readonly ILogger _logger;
	private readonly IConfigService _configService;
	private readonly SourceList<RoomStatus> _roomList;
	private readonly StartupService _startup;

	public readonly Config Config;
	private readonly string _startUpData = $"""
		"{Environment.ProcessPath}" {Constants.ParameterSilent}
		""";

	public SettingViewModel(
		IScreen hostScreen,
		ILogger<SettingViewModel> logger,
		IConfigService configService,
		Config config,
		SourceList<RoomStatus> roomList,
		StartupService startup)
	{
		HostScreen = hostScreen;
		_logger = logger;
		_configService = configService;
		Config = config;
		_roomList = roomList;
		_startup = startup;

		SelectMainDirCommand = ReactiveCommand.Create(SelectDirectory);
		OpenMainDirCommand = ReactiveCommand.CreateFromObservable(OpenDirectory);
		CheckUpdateCommand = ReactiveCommand.CreateFromTask(CheckUpdateAsync);

		InitAsync().Forget();
	}

	private async ValueTask InitAsync()
	{
		await _configService.LoadAsync(default);
		await Task.Delay(TimeSpan.FromSeconds(1)); // Wait apiClient to load settings

		_roomList.AddRange(Config.Rooms);

		if (Config.IsCheckUpdateOnStart)
		{
			await CheckUpdateCommand.Execute();
		}
	}

	private void SelectDirectory()
	{
		OpenFolderDialog dlg = new()
		{
			InitialDirectory = Config.MainDir
		};
		if (dlg.ShowDialog() is true)
		{
			Config.MainDir = dlg.FolderName;
		}
	}

	private IObservable<Unit> OpenDirectory()
	{
		return Observable.Start(() =>
		{
			FileUtils.OpenDir(Config.MainDir);
			return Unit.Default;
		});
	}

	private async Task CheckUpdateAsync(CancellationToken token)
	{
		try
		{
			UpdateStatus = @"正在检查更新...";
			string version = Utils.Utils.GetAppVersion()!;
			GitHubReleasesUpdateChecker updateChecker = new(
				@"HMBSbige",
				@"BilibiliLiveRecordDownLoader",
				Config.IsCheckPreRelease,
				version
			);
			if (await updateChecker.CheckAsync(HttpClientUtils.BuildClient(Config.Cookie, Config.UserAgent ?? Config.DefaultUserAgent, Config.HttpHandler), token))
			{
				if (updateChecker.LatestVersionUrl is null)
				{
					UpdateStatus = @"更新地址获取出错";
					return;
				}

				UpdateStatus = $@"发现新版本：{updateChecker.LatestVersion}";
				using DisposableContentDialog dialog = new();
				dialog.Title = UpdateStatus;
				dialog.Content = @"是否跳转到下载页？";
				dialog.PrimaryButtonText = @"是";
				dialog.SecondaryButtonText = @"否";
				dialog.DefaultButton = ContentDialogButton.Primary;
				if (await dialog.SafeShowAsync() == ContentDialogResult.Primary)
				{
					FileUtils.OpenUrl(updateChecker.LatestVersionUrl);
				}
			}
			else
			{
				UpdateStatus = $@"没有找到新版本：{version} ≥ {updateChecker.LatestVersion}";
			}
		}
		catch (Exception ex)
		{
			UpdateStatus = @"检查更新出错";
			_logger.LogError(ex, UpdateStatus);
		}
	}

	public IDisposable CreateDiskMonitor()
	{
		return Observable.Interval(TimeSpan.FromSeconds(1), RxApp.MainThreadScheduler).Subscribe(GetDiskUsage);
	}

	private void GetDiskUsage(long _)
	{
		(ulong availableFreeSpace, ulong totalSize, ulong totalFree) = FileUtils.GetDiskUsage(Config.MainDir);
		if (totalSize != 0)
		{
			ulong usedSize = totalSize - totalFree;
			DiskUsageProgressBarText = $@"已使用 {usedSize.ToHumanBytesString()}/{totalSize.ToHumanBytesString()} 剩余可用 {availableFreeSpace.ToHumanBytesString()}";
			double percentage = usedSize / (double)totalSize;
			DiskUsageProgressBarValue = percentage * 100;
		}
		else
		{
			DiskUsageProgressBarText = string.Empty;
			DiskUsageProgressBarValue = 0;
		}
	}

	public void CheckStartupStatus()
	{
		try
		{
			IsRunOnStartup = _startup.Check(_startUpData);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"检查自启动状态失败");
		}
	}

	private void SetStartup()
	{
		try
		{
			_startup.Set(_startUpData);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"设置自启动失败");
		}
	}

	private void RemoveStartup()
	{
		try
		{
			_startup.Delete();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, @"取消自启动失败");
		}
	}

	public void SwitchStartup(bool enable)
	{
		if (enable)
		{
			SetStartup();
		}
		else
		{
			RemoveStartup();
		}
	}
}
