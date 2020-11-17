using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Shared;
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

		#region Command

		public ReactiveCommand<Unit, Unit> SelectMainDirCommand { get; }
		public ReactiveCommand<Unit, Unit> OpenMainDirCommand { get; }
		public ReactiveCommand<Unit, Unit> CheckUpdateCommand { get; }

		#endregion

		private readonly ILogger _logger;
		private readonly IConfigService _configService;
		public readonly GlobalViewModel Global;

		public Config Config => _configService.Config;

		public SettingViewModel(
			IScreen hostScreen,
			ILogger<MainWindowViewModel> logger,
			IConfigService configService,
			GlobalViewModel global)
		{
			HostScreen = hostScreen;
			_logger = logger;
			_configService = configService;
			Global = global;

			InitAsync().NoWarning();

			SelectMainDirCommand = ReactiveCommand.Create(SelectDirectory);
			OpenMainDirCommand = ReactiveCommand.CreateFromObservable(OpenDirectory);
			CheckUpdateCommand = ReactiveCommand.CreateFromTask(CheckUpdateAsync);
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
				Global.UpdateStatus = @"正在检查更新...";
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
						Global.UpdateStatus = @"更新地址获取出错";
						return;
					}

					Global.UpdateStatus = $@"发现新版本：{updateChecker.LatestVersion}";
					using var dialog = new DisposableContentDialog
					{
						Title = Global.UpdateStatus,
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
					Global.UpdateStatus = $@"没有找到新版本：{version} ≥ {updateChecker.LatestVersion}";
				}
			}
			catch (Exception ex)
			{
				Global.UpdateStatus = @"检查更新出错";
				_logger.LogError(ex, Global.UpdateStatus);
			}
		}
	}
}
