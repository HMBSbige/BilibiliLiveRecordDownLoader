using BilibiliLiveRecordDownLoader.ViewModels.Platform;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed class UnsupportedAutoStart : IAutoStartService
{
	public bool IsSupported => false;

	public bool IsEnabled()
	{
		return false;
	}

	public void SetEnabled(bool enabled)
	{
		throw new NotSupportedException();
	}

	public void TakeOverExisting()
	{
	}
}
