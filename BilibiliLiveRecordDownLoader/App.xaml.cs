using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Utils;
using ReactiveMarbles.ObservableEvents;
using ReactiveUI;
using Serilog;
using SingleInstance;
using System.IO;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;

namespace BilibiliLiveRecordDownLoader;

public partial class App
{
	private readonly CompositeDisposable _disposable;
	private readonly SingleInstanceService _singleInstance;

	public App()
	{
		try
		{
#if DEBUG
			const string identifier = $@"Global\{nameof(BilibiliLiveRecordDownLoader)}_Debug";
#else
			const string identifier = $@"Global\{nameof(BilibiliLiveRecordDownLoader)}";
#endif
			_disposable = new CompositeDisposable();
			_singleInstance = new SingleInstanceService(identifier).DisposeWith(_disposable);

			string dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
			Environment.CurrentDirectory = Path.GetFullPath(dir);

			DI.CreateLogger();
		}
		catch (Exception ex)
		{
			MessageBox.Show($@"WTF??? {ex}", nameof(BilibiliLiveRecordDownLoader), MessageBoxButton.OK, MessageBoxImage.Error);
			Environment.Exit(1);
		}
	}

	private async void Application_Startup(object sender, StartupEventArgs e)
	{
		Current.Events().DispatcherUnhandledException.Subscribe(args => UnhandledException(args.Exception));

		if (!_singleInstance.TryStartSingleInstance())
		{
			if (await SendShowCommandAsync())
			{
				Current.Shutdown(0);
			}
			else
			{
				Current.Shutdown(2);
			}
			return;
		}

		_singleInstance.Received.ObserveOn(RxApp.TaskpoolScheduler).Subscribe(ArgumentsReceived).DisposeWith(_disposable);
		_singleInstance.StartListenServer();

		DI.Register();

		MainWindow = DI.GetRequiredService<MainWindow>();
		if (e.Args.Contains(Constants.ParameterSilent))
		{
			MainWindow.Visibility = Visibility.Hidden;
		}
		MainWindow.ShowWindow();

		void UnhandledException(Exception ex)
		{
			try
			{
				Log.Fatal(ex, @"未捕获异常");
				MessageBox.Show($@"未捕获异常：{ex}", nameof(BilibiliLiveRecordDownLoader), MessageBoxButton.OK, MessageBoxImage.Error);
			}
			finally
			{
				Current.Shutdown(1);
			}
		}

		async ValueTask<bool> SendShowCommandAsync()
		{
			try
			{
				string response = await _singleInstance.SendMessageToFirstInstanceAsync(Constants.ParameterShow);

				if (response is Constants.ParameterShow)
				{
					return true;
				}

				throw new Exception($@"Receive error message: {response}");
			}
			catch (Exception)
			{
				return false;
			}
		}

		void ArgumentsReceived((string, Action<string>) receive)
		{
			(string message, Action<string> endFunc) = receive;
			HashSet<string> args = message
				.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.ToHashSet();

			if (args.Contains(Constants.ParameterShow))
			{
				RxApp.MainThreadScheduler.Schedule(() => DI.GetRequiredService<MainWindow>().ShowWindow());
				endFunc(Constants.ParameterShow);
				return;
			}

			endFunc(@"???");
		}
	}

	protected override void OnExit(ExitEventArgs e)
	{
		base.OnExit(e);

		_disposable.Dispose();
		Log.CloseAndFlush();

		Environment.Exit(e.ApplicationExitCode);
	}
}
