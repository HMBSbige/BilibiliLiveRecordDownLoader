namespace BilibiliLiveRecordDownLoader.ViewModels;

public abstract class ViewModelBase : ReactiveObject, IDisposable
{
	private readonly CancellationTokenSource _lifetime = new();

	protected MultipleDisposable Disposables { get; } = new();

	protected void PersistToConfig<T>(IObservable<T> source, Action<AppConfig, T> updateAction)
	{
		PersistToConfig(AppLocator.Current.GetRequiredService<IAppConfigManager>(), source, updateAction);
	}

	protected void PersistToConfig<T>(IAppConfigManager configManager, IObservable<T> source, Action<AppConfig, T> updateAction)
	{
		source
			.Skip(1)
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

	// 视图模型释放时取消，异常交给全局处理
	protected void Forget(Func<CancellationToken, Task> taskFactory)
	{
		_ = RunAsync(taskFactory, _lifetime.Token);

		static async Task RunAsync(Func<CancellationToken, Task> taskFactory, CancellationToken cancellationToken)
		{
			try
			{
				await taskFactory(cancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception exception)
			{
				RxState.DefaultExceptionHandler.OnNext(exception);
			}
		}
	}

	// 应用服务以版本号暴露状态变化，界面按固定间隔比较版本号后再刷新
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

	// 视图模型里预先格式化的本地化文本需要在切换语言后重新生成
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
