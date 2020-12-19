using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using BilibiliLiveRecordDownLoader.Views.Dialogs;
using DynamicData;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAPICodePack.Dialogs;
using ModernWpf.Controls;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using RunAtStartup;
using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using UpdateChecker;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
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

			InitAsync().NoWarning();
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
			var dlg = new CommonOpenFileDialog
			{
				IsFolderPicker = true,
				Multiselect = false,
				Title = @"选择存储目录",
				AddToMostRecentlyUsedList = false,
				EnsurePathExists = true,
				NavigateToShortcut = true,
				InitialDirectory = Config.MainDir
			};
			if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
			{
				Config.MainDir = dlg.FileName;
			}
		}

		private IObservable<Unit> OpenDirectory()
		{
			return Observable.Start(() =>
			{
				Utils.Utils.OpenDir(Config.MainDir);
				return Unit.Default;
			});
		}

		private async Task CheckUpdateAsync(CancellationToken token)
		{
			try
			{
				UpdateStatus = @"正在检查更新...";
				var version = Utils.Utils.GetAppVersion()!;
				var updateChecker = new GitHubReleasesUpdateChecker(
						@"HMBSbige",
						@"BilibiliLiveRecordDownLoader",
						Config.IsCheckPreRelease,
						version
				);
				if (await updateChecker.CheckAsync(HttpClientUtils.BuildClient(Config.Cookie, Config.UserAgent, Config.HttpHandler), token))
				{
					if (updateChecker.LatestVersionUrl is null)
					{
						UpdateStatus = @"更新地址获取出错";
						return;
					}

					UpdateStatus = $@"发现新版本：{updateChecker.LatestVersion}";
					using var dialog = new DisposableContentDialog
					{
						Title = UpdateStatus,
						Content = @"是否跳转到下载页？",
						PrimaryButtonText = @"是",
						SecondaryButtonText = @"否",
						DefaultButton = ContentDialogButton.Primary
					};
					if (await dialog.ShowAsync() == ContentDialogResult.Primary)
					{
						Utils.Utils.OpenUrl(updateChecker.LatestVersionUrl);
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
			var (availableFreeSpace, totalSize) = Utils.Utils.GetDiskUsage(Config.MainDir);
			if (totalSize != 0)
			{
				DiskUsageProgressBarText = $@"已使用 {Utils.Utils.CountSize(totalSize - availableFreeSpace)}/{Utils.Utils.CountSize(totalSize)} 剩余 {Utils.Utils.CountSize(availableFreeSpace)}";
				var percentage = (totalSize - availableFreeSpace) / (double)totalSize;
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
				IsRunOnStartup = _startup.Check();
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
				var data = $@"""{Utils.Utils.GetExecutablePath()}"" {Utils.Constants.ParameterSilent}";
				_startup.Set(data);
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
}
