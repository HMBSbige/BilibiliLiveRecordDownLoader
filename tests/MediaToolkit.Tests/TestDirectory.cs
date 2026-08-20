namespace MediaToolkit.Tests;

internal sealed class TestDirectory : IDisposable
{
	// 名字带空格和非 ASCII 字符，顺带验证参数传递和编码
	public string Root { get; } = Path.Combine(Path.GetTempPath(), @"MediaToolkit.Tests", $@"media toolkit 测试 {Guid.CreateVersion7():N}");

	public TestDirectory()
	{
		Directory.CreateDirectory(Root);
	}

	public string Combine(params ReadOnlySpan<string> paths)
	{
		return Path.Combine([Root, .. paths]);
	}

	public string CreateFile(string relativePath, byte[]? content = null)
	{
		string path = Combine(relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Root);
		File.WriteAllBytes(path, content ?? []);
		return path;
	}

	public void Dispose()
	{
		Directory.Delete(Root, true);
	}
}
