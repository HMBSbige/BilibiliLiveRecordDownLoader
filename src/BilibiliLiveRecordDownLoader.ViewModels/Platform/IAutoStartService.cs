namespace BilibiliLiveRecordDownLoader.ViewModels.Platform;

public interface IAutoStartService
{
	bool IsSupported { get; }

	bool IsEnabled();

	void SetEnabled(bool enabled);

	// 已经登记过时改成启动当前程序，没有登记时不新建；旧版登记用的也是同一个名字
	void TakeOverExisting();
}
