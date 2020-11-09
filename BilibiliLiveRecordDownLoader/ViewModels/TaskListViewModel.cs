using BilibiliLiveRecordDownLoader.Shared.Interfaces;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System.Threading;

namespace BilibiliLiveRecordDownLoader.ViewModels
{
    public class TaskListViewModel : MyReactiveObject
    {
        private readonly ILogger _logger;

        #region 字段

        private double _progress;
        private string _speed;
        private string _status;
        private string _description;

        #endregion

        #region 属性

        /// <summary>
        /// 描述
        /// </summary>
        public string Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        /// <summary>
        /// 进度，[0.0,1.0]
        /// </summary>
        public double Progress
        {
            get => _progress;
            set => this.RaiseAndSetIfChanged(ref _progress, value);
        }

        /// <summary>
        /// 速度
        /// </summary>
        public string Speed
        {
            get => _speed;
            set => this.RaiseAndSetIfChanged(ref _speed, value);
        }

        /// <summary>
        /// 状态
        /// </summary>
        public string Status
        {
            get => _status;
            set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        #endregion

        private IProgress _task;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public TaskListViewModel(ILogger logger, IProgress task, string description)
        {
            _logger = logger;
            _task = task;
            Description = description;
        }
    }
}
