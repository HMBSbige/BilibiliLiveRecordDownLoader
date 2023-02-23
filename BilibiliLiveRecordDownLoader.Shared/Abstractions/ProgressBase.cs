using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace BilibiliLiveRecordDownLoader.Shared.Abstractions;

public abstract class ProgressBase : IProgress, IAsyncDisposable
{
	protected long FileSize;
	protected long Current;
	protected long Last;

	public double Progress => Interlocked.Read(ref Current) / (double)FileSize;

	protected readonly BehaviorSubject<double> CurrentSpeedSubject = new(0.0);
	public IObservable<double> CurrentSpeed => CurrentSpeedSubject.AsObservable();

	protected readonly BehaviorSubject<string> StatusSubject = new(string.Empty);
	public IObservable<string> Status => StatusSubject.AsObservable();

	public virtual ValueTask DisposeAsync()
	{
		CurrentSpeedSubject.OnCompleted();
		StatusSubject.OnCompleted();

		return default;
	}

	protected IDisposable CreateSpeedMonitor()
	{
		Stopwatch sw = Stopwatch.StartNew();
		return Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ =>
		{
			long last = Interlocked.Read(ref Last);
			CurrentSpeedSubject.OnNext(last / sw.Elapsed.TotalSeconds);
			sw.Restart();
			Interlocked.Add(ref Last, -last);
		});
	}
}
