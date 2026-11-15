namespace BilibiliLiveRecordDownLoader.Application.Tests;

internal sealed class TestDirectory : IDisposable
{
	public string Root { get; } = Path.Combine(Path.GetTempPath(), "BilibiliLiveRecordDownLoader.Tests", Guid.CreateVersion7().ToString("N"));

	public TestDirectory()
	{
		Directory.CreateDirectory(Root);
	}

	public string Combine(string relativePath)
	{
		return Path.Combine(Root, relativePath);
	}

	public void Dispose()
	{
		Directory.Delete(Root, true);
	}
}
