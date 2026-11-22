namespace BilibiliLiveRecordDownLoader.ViewModels;

public abstract class ViewModelBase : ReactiveObject, IDisposable
{
	private readonly CancellationTokenSource _lifetime = new();

	protected MultipleDisposable Disposables { get; } = new();

	protected void PersistToConfig<T>(IObservable<T> source, Action<AppConfig, T> updateAction)
	{
		PersistToConfig(AppLocator.Current.GetRequiredService<IAppConfigManager>(), source, updateAction);
	}

	// quietPeriod 为正时，值停下来这么久之后才保存
	protected void PersistToConfig<T>(IAppConfigManager configManager, IObservable<T> source, Action<AppConfig, T> updateAction, TimeSpan quietPeriod = default)
	{
		IObservable<T> changes = source.Skip(1);

		if (quietPeriod > TimeSpan.Zero)
		{
			changes = changes.Calm(quietPeriod, RxSchedulers.TaskpoolScheduler);
		}

		changes
			.Unique()
			.Map
			(value => HandleErrors
				(
					Signal.FromAsync
					(async cancellationToken =>
						{
							await configManager.UpdateAsync(config => updateAction(config, value), cancellationToken);
							return RxVoid.Default;
						}
					)
				)
			)
			.SwitchTo()
			.Subscribe()
			.DisposeWith(Disposables);
	}

	// 不是由释放引起的取消异常（例如请求超时）也交给全局处理
	protected void Forget(Func<CancellationToken, Task> taskFactory)
	{
		_ = RunAsync(taskFactory, _lifetime.Token);

		static async Task RunAsync(Func<CancellationToken, Task> taskFactory, CancellationToken cancellationToken)
		{
			try
			{
				await taskFactory(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
			}
			catch (Exception exception)
			{
				RxState.DefaultExceptionHandler.OnNext(exception);
			}
		}
	}

	protected void PollChanges(Func<long> getVersion, Action apply, TimeSpan interval)
	{
		apply();

		Signal.Every(interval, RxSchedulers.TaskpoolScheduler)
			.Map(_ => getVersion())
			.Unique()
			.ObserveOn(RxSchedulers.MainThreadScheduler)
			.Subscribe(_ => apply())
			.DisposeWith(Disposables);
	}

	// 查询可能涉及磁盘或网络，首次也在后台执行
	protected void Poll<T>(Func<T> getState, Action<T> apply, TimeSpan interval)
	{
		Signal.Timer(TimeSpan.Zero, interval, RxSchedulers.TaskpoolScheduler)
			.Map(_ => getState())
			.ObserveOn(RxSchedulers.MainThreadScheduler)
			.Subscribe(apply)
			.DisposeWith(Disposables);
	}

	protected void OnLanguageChanged(Action apply)
	{
		Signal.FromEventPattern(static handler => AppLanguage.CurrentChanged += handler, static handler => AppLanguage.CurrentChanged -= handler)
			.Subscribe(_ => apply())
			.DisposeWith(Disposables);
	}

	private static IObservable<T> HandleErrors<T>(IObservable<T> source)
	{
		return source.Recover
		(
			static exception =>
			{
				if (exception is not OperationCanceledException)
				{
					RxState.DefaultExceptionHandler.OnNext(exception);
				}

				return Signal.None<T>();
			}
		);
	}

	public virtual void Dispose()
	{
		_lifetime.Cancel();
		Disposables.Dispose();
		GC.SuppressFinalize(this);
	}
}
