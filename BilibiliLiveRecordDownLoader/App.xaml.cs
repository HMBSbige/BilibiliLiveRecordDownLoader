using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Utils;
using Serilog;
using SingleInstance;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Windows;

namespace BilibiliLiveRecordDownLoader
{
	public partial class App
	{
		private readonly SingleInstanceService _singleInstance;

		public App()
		{
			try
			{
#if DEBUG
				var identifier = $@"Global\{nameof(BilibiliLiveRecordDownLoader)}_Debug";
#else
				var identifier = $@"Global\{nameof(BilibiliLiveRecordDownLoader)}";
#endif
				_singleInstance = new(identifier);

				Directory.SetCurrentDirectory(Path.GetDirectoryName(Utils.Utils.GetExecutablePath())!);

				DI.CreateLogger();
			}
			catch (Exception ex)
			{
				MessageBox.Show($@"WTF??? {ex}", nameof(BilibiliLiveRecordDownLoader), MessageBoxButton.OK, MessageBoxImage.Error);
				Environment.Exit(1);
			}
		}

		private void Application_Startup(object sender, StartupEventArgs e)
		{
			Current.Events().Exit.Subscribe(args => AppExit(args.ApplicationExitCode));
			Current.Events().DispatcherUnhandledException.Subscribe(args => UnhandledException(args.Exception));

			if (!_singleInstance.IsFirstInstance)
			{
				_singleInstance.PassArgumentsToFirstInstance(e.Args.Append(Constants.ParameterShow));
				AppExit(0);
				return;
			}

			_singleInstance.ArgumentsReceived.ObserveOnDispatcher().Subscribe(SingleInstance_ArgumentsReceived);
			_singleInstance.ListenForArgumentsFromSuccessiveInstances();

			DI.Register();

			MainWindow = DI.GetRequiredService<MainWindow>();
			if (e.Args.Contains(Constants.ParameterSilent))
			{
				MainWindow.Visibility = Visibility.Hidden;
			}
			MainWindow.ShowWindow();
		}

		private void UnhandledException(Exception ex)
		{
			try
			{
				Log.Fatal(ex, @"未捕获异常");
				MessageBox.Show($@"未捕获异常：{ex}", nameof(BilibiliLiveRecordDownLoader), MessageBoxButton.OK, MessageBoxImage.Error);
			}
			finally
			{
				AppExit(1);
			}
		}

		private void SingleInstance_ArgumentsReceived(IEnumerable<string> args)
		{
			if (args.Contains(Constants.ParameterShow))
			{
				MainWindow?.ShowWindow();
			}
		}

		private void AppExit(int exitCode)
		{
			_singleInstance.Dispose();
			Log.CloseAndFlush();
			Current.Shutdown(exitCode);
			if (exitCode != 0)
			{
				Environment.Exit(exitCode);
			}
		}
	}
}
