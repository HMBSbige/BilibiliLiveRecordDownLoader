using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.Shared.Abstracts
{
	public abstract class ProgressBase : IProgress
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
			var sw = Stopwatch.StartNew();
			return Observable.Interval(TimeSpan.FromSeconds(1)).Subscribe(_ =>
			{
				var last = Interlocked.Read(ref Last);
				CurrentSpeedSubject.OnNext(last / sw.Elapsed.TotalSeconds);
				sw.Restart();
				Interlocked.Add(ref Last, -last);
			});
		}
	}
}
