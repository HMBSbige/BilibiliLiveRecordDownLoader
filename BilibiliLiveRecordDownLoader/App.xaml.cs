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
using System.Threading;
using System.Windows;

namespace BilibiliLiveRecordDownLoader
{
	public partial class App
	{
		private static int _exited;

		private void Application_Startup(object sender, StartupEventArgs e)
		{
			Directory.SetCurrentDirectory(Path.GetDirectoryName(Utils.Utils.GetExecutablePath())!);
#if DEBUG
			var identifier = $@"Global\{nameof(BilibiliLiveRecordDownLoader)}_Debug";
#else
			var identifier = $@"Global\{nameof(BilibiliLiveRecordDownLoader)}";
#endif
			var singleInstance = new SingleInstance.SingleInstance(identifier);
			if (!singleInstance.IsFirstInstance)
			{
				singleInstance.PassArgumentsToFirstInstance(e.Args.Append(Constants.ParameterShow));
				Current.Shutdown();
				return;
			}

			singleInstance.ArgumentsReceived.ObserveOnDispatcher().Subscribe(SingleInstance_ArgumentsReceived);
			singleInstance.ListenForArgumentsFromSuccessiveInstances();

			Current.Events().Exit.Subscribe(_ =>
			{
				singleInstance.Dispose();
				Log.CloseAndFlush();
			});
			Current.Events().DispatcherUnhandledException.Subscribe(args =>
			{
				try
				{
					if (Interlocked.Increment(ref _exited) != 1)
					{
						return;
					}

					var exStr = $@"未捕获异常：{args.Exception}";

					Log.Fatal(args.Exception, @"未捕获异常");
					MessageBox.Show(exStr, nameof(BilibiliLiveRecordDownLoader), MessageBoxButton.OK, MessageBoxImage.Error);

					Current.Shutdown();
				}
				finally
				{
					singleInstance.Dispose();
					Log.CloseAndFlush();
				}
			});

			ThemeManager.Current.ApplicationTheme = null;

			Register();

			MainWindow = Locator.Current.GetService<MainWindow>();
			if (e.Args.Contains(Constants.ParameterSilent))
			{
				MainWindow.Visibility = Visibility.Hidden;
			}
			MainWindow.ShowWindow();
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
					.AddLogging(c => c.AddSerilog());
		}

		private static void Register()
		{
			var memorySink = new SubjectMemorySink(Constants.OutputTemplate);
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
				.WriteTo.Async(c => c.Sink(memorySink))
				.CreateLogger();

			var services = new ServiceCollection();

			services.UseMicrosoftDependencyResolver();
			Locator.CurrentMutable.InitializeSplat();
			Locator.CurrentMutable.InitializeReactiveUI(RegistrationNamespace.Wpf);

			ConfigureServices(services);
			services.TryAddSingleton(memorySink);
		}
	}
}
