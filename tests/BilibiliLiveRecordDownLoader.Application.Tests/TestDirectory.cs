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

	public string WriteAllBytes(string relativePath, byte[] content)
	{
		string path = Combine(relativePath);
		File.WriteAllBytes(path, content);
		return path;
	}

	public void Dispose()
	{
		Directory.Delete(Root, true);
	}
}
