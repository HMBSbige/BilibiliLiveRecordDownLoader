using BilibiliLiveRecordDownLoader.Interfaces;
using ReactiveUI;
using System.Threading.Tasks;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
	public abstract class TaskListViewModel : MyReactiveObject, ITask
	{
		#region 字段

		private double _progress;
		private string? _speed;
		private string? _status;
		private string? _description;

		#endregion

		#region 属性

		public string? Description
		{
			get => _description;
			set => this.RaiseAndSetIfChanged(ref _description, value);
		}

		public double Progress
		{
			get => _progress;
			set => this.RaiseAndSetIfChanged(ref _progress, value);
		}

		public string? Speed
		{
			get => _speed;
			set => this.RaiseAndSetIfChanged(ref _speed, value);
		}

		public string? Status
		{
			get => _status;
			set => this.RaiseAndSetIfChanged(ref _status, value);
		}

		#endregion

		public abstract ValueTask StartAsync();

		public abstract void Stop();
	}
}
