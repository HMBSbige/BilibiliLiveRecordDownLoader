using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;

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
	}
}
