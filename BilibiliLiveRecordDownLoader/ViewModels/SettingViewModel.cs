using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using BilibiliLiveRecordDownLoader.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAPICodePack.Dialogs;
using ModernWpf.Controls;
using ReactiveUI;
using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using UpdateChecker;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
#pragma warning disable CS8612
	public class SettingViewModel : ReactiveObject, IRoutableViewModel
#pragma warning restore CS8612
	{
		public string UrlPathSegment => @"Settings";

		public IScreen HostScreen { get; }

		#region 字段

		private string? _diskUsageProgressBarText;
		private double _diskUsageProgressBarValue;
		private string? _updateStatus;

		#endregion

		#region 属性

		public string? DiskUsageProgressBarText
		{
			get => _diskUsageProgressBarText;
			set => this.RaiseAndSetIfChanged(ref _diskUsageProgressBarText, value);
		}

		public double DiskUsageProgressBarValue
		{
			get => _diskUsageProgressBarValue;
			set => this.RaiseAndSetIfChanged(ref _diskUsageProgressBarValue, value);
		}

		public string? UpdateStatus
		{
			get => _updateStatus;
			set => this.RaiseAndSetIfChanged(ref _updateStatus, value);
		}

		#endregion

		#region Command

		public ReactiveCommand<Unit, Unit> SelectMainDirCommand { get; }
		public ReactiveCommand<Unit, Unit> OpenMainDirCommand { get; }
		public ReactiveCommand<Unit, Unit> CheckUpdateCommand { get; }

		#endregion

		private readonly ILogger _logger;
		private readonly IConfigService _configService;

		public Config Config => _configService.Config;

		public SettingViewModel(
			IScreen hostScreen,
			ILogger<SettingViewModel> logger,
			IConfigService configService)
		{
			HostScreen = hostScreen;
			_logger = logger;
			_configService = configService;

			SelectMainDirCommand = ReactiveCommand.Create(SelectDirectory);
			OpenMainDirCommand = ReactiveCommand.CreateFromObservable(OpenDirectory);
			CheckUpdateCommand = ReactiveCommand.CreateFromTask(CheckUpdateAsync);

			InitAsync().NoWarning();
		}

		private async ValueTask InitAsync()
		{
			await _configService.LoadAsync(default);
			if (_configService.Config.IsCheckUpdateOnStart)
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
				InitialDirectory = _configService.Config.MainDir
			};
			if (dlg.ShowDialog() == CommonFileDialogResult.Ok)
			{
				_configService.Config.MainDir = dlg.FileName;
			}
		}

		private IObservable<Unit> OpenDirectory()
		{
			return Observable.Start(() =>
			{
				Utils.Utils.OpenDir(_configService.Config.MainDir);
				return Unit.Default;
			});
		}

		private async Task CheckUpdateAsync()
		{
			try
			{
				UpdateStatus = @"正在检查更新...";
				var version = Utils.Utils.GetAppVersion()!;
				var updateChecker = new GitHubReleasesUpdateChecker(
						@"HMBSbige",
						@"BilibiliLiveRecordDownLoader",
						_configService.Config.IsCheckPreRelease,
						version
				);
				if (await updateChecker.CheckAsync(default))
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
			var (availableFreeSpace, totalSize) = Utils.Utils.GetDiskUsage(_configService.Config.MainDir);
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
	}
}
