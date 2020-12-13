using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModernWpf;
using ReactiveUI;
using Serilog;
using Serilog.Events;
using Splat;
using Splat.Microsoft.Extensions.DependencyInjection;
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
		private readonly SingleInstance.SingleInstance _singleInstance;
		private readonly SubjectMemorySink _memorySink;

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

				_memorySink = new SubjectMemorySink(Constants.OutputTemplate);
				CreateLogger();
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

			Directory.SetCurrentDirectory(Path.GetDirectoryName(Utils.Utils.GetExecutablePath())!);

			if (!_singleInstance.IsFirstInstance)
			{
				_singleInstance.PassArgumentsToFirstInstance(e.Args.Append(Constants.ParameterShow));
				AppExit(0);
			}

			_singleInstance.ArgumentsReceived.ObserveOnDispatcher().Subscribe(SingleInstance_ArgumentsReceived);
			_singleInstance.ListenForArgumentsFromSuccessiveInstances();

			ThemeManager.Current.ApplicationTheme = null;

			Register().TryAddSingleton(_memorySink);

			MainWindow = Locator.Current.GetService<MainWindow>();
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

		private static void ConfigureServices(IServiceCollection services)
		{
			services.AddViewModels()
					.AddViews()
					.AddDanmuClients()
					.AddConfig()
					.AddDynamicData()
					.AddFlvProcessor()
					.AddStartupService()
					.AddGlobalTaskQueue()
					.AddBilibiliApiClient()
					.AddHttpDownloader()
					.AddLogging(c => c.AddSerilog());
		}

		private void CreateLogger()
		{
			Log.Logger = new LoggerConfiguration()
#if DEBUG
				.MinimumLevel.Debug()
				.WriteTo.Async(c => c.Debug(outputTemplate: Constants.OutputTemplate))
#else
				.MinimumLevel.Information()
#endif
				.MinimumLevel.Override(@"Microsoft", LogEventLevel.Information)
				.Enrich.FromLogContext()
				.WriteTo.Async(c => c.File(Constants.LogFile,
						outputTemplate: Constants.OutputTemplate,
						rollingInterval: RollingInterval.Day,
						fileSizeLimitBytes: Constants.MaxLogFileSize))
				.WriteTo.Async(c => c.Sink(_memorySink))
				.CreateLogger();
		}

		private static IServiceCollection Register()
		{
			var services = new ServiceCollection();

			services.UseMicrosoftDependencyResolver();
			Locator.CurrentMutable.InitializeSplat();
			Locator.CurrentMutable.InitializeReactiveUI(RegistrationNamespace.Wpf);

			ConfigureServices(services);

			return services;
		}

		private void AppExit(int exitCode)
		{
			_singleInstance.Dispose();
			Log.CloseAndFlush();
			Current.Shutdown(exitCode);
			Environment.Exit(exitCode);
		}
	}
}
