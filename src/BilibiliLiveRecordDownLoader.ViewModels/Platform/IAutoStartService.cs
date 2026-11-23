namespace BilibiliLiveRecordDownLoader.ViewModels.Platform;

public interface IAutoStartService
{
	bool IsSupported { get; }

	bool IsEnabled();

	void SetEnabled(bool enabled);

	// 只改写已有的登记（旧版也用这个名字），没有登记时不新建
	void TakeOverExisting();
}
