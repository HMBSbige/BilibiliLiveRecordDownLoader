namespace BilibiliLiveRecordDownLoader.Interfaces;

public interface ITask
{
	/// <summary>
	/// 描述
	/// </summary>
	string? Description { get; }

	/// <summary>
	/// 进度，[0.0,1.0]
	/// </summary>
	double Progress { get; }

	/// <summary>
	/// 速度
	/// </summary>
	string? Speed { get; }

	/// <summary>
	/// 状态
	/// </summary>
	string? Status { get; }

	Task StartAsync();
	void Stop();
}
