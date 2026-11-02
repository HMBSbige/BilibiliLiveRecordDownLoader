namespace BilibiliLiveRecordDownLoader.Application.Contracts.Platform;

public interface IAutoStartService
{
	bool IsSupported { get; }

	bool IsEnabled();

	void SetEnabled(bool enabled);
}
