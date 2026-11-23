using System.Diagnostics.CodeAnalysis;

namespace BilibiliLiveRecordDownLoader.ViewModels;

public abstract class ViewModelBase : ReactiveObject, IDisposable
{
	[SuppressMessage("Usage", "CA2213", Justification = "没有定时器和链接，不需要释放，释放后再次 Dispose 时 Cancel 会抛异常")]
	private readonly CancellationTokenSource _lifetime = new();

	protected MultipleDisposable Disposables { get; } = [];

	protected void PersistToConfig<T>(IAppConfigManager configManager, IObservable<T> source, Action<AppConfig, T> updateAction, TimeSpan quietPeriod = default)
	{
		IObservable<T> changes = source.Skip(1);

		if (quietPeriod > TimeSpan.Zero)
		{
			changes = changes.Calm(quietPeriod, RxSchedulers.TaskpoolScheduler);
		}

		RunLatest(changes.Unique(), (value, cancellationToken) => configManager.UpdateAsync(config => updateAction(config, value), cancellationToken).AsTask());
	}

	protected void RunLatest<T>(IObservable<T> source, Func<T, CancellationToken, Task> action)
	{
		source
			.SwitchMap(value => Signal.FromAsync(cancellationToken => RunAsync(token => action(value, token), cancellationToken)))
			.Subscribe()
			.DisposeWith(Disposables);
	}

	protected void Forget(Func<CancellationToken, Task> taskFactory)
	{
		_ = RunAsync(taskFactory, _lifetime.Token);
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

	// 不是由自身取消引起的取消异常（例如请求超时）也交给全局处理
	private static async Task<RxVoid> RunAsync(Func<CancellationToken, Task> taskFactory, CancellationToken cancellationToken)
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

		return RxVoid.Default;
	}

	public virtual void Dispose()
	{
		_lifetime.Cancel();
		Disposables.Dispose();
		GC.SuppressFinalize(this);
	}
}
