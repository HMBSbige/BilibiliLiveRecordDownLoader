using MediaToolkit.FFmpeg;

namespace MediaToolkit.Tests.FFmpeg;

public class FFmpegLocateTest
{
	private static readonly string ExecutableName = FFmpegRunner.ExecutableName;

	private static readonly string CommandFile = OperatingSystem.IsWindows() ? @"ffmpeg7.exe" : @"ffmpeg7";

	private static string JoinPath(params string[] directories)
	{
		return string.Join(Path.PathSeparator, directories);
	}

	[Test]
	public async Task PreferredExistingFileReturnsFullPathAsync()
	{
		using TestDirectory directory = new();
		string custom = directory.CreateFile(Path.Combine(@"tools", @"my-ffmpeg.bin"));
		string relative = Path.GetRelativePath(Environment.CurrentDirectory, custom);

		string? located = FFmpegRunner.Locate(relative, directory.Combine(@"app"), null, []);

		await Assert.That(located).IsEqualTo(custom);
	}

	[Test]
	public async Task PreferredMissingFilePathReturnsNullEvenIfPathHasFFmpegAsync()
	{
		using TestDirectory directory = new();
		directory.CreateFile(Path.Combine(@"bin", ExecutableName));

		string? located = FFmpegRunner.Locate(directory.Combine(@"missing", ExecutableName), directory.Root, directory.Combine(@"bin"), []);

		await Assert.That(located).IsNull();
	}

	[Test]
	public async Task PreferredCommandNameIgnoresApplicationDirectoryAsync()
	{
		using TestDirectory directory = new();
		directory.CreateFile(Path.Combine(@"app", CommandFile));
		string expected = directory.CreateFile(Path.Combine(@"second", CommandFile));

		string? located = FFmpegRunner.Locate(@"ffmpeg7", directory.Combine(@"app"), JoinPath(directory.Combine(@"first"), directory.Combine(@"second")), []);

		await Assert.That(located).IsEqualTo(expected);
	}

	[Test]
	public async Task PreferredCommandNameFallsBackToPlatformDirectoriesAsync()
	{
		using TestDirectory directory = new();
		string expected = directory.CreateFile(Path.Combine(@"platform", CommandFile));

		string? located = FFmpegRunner.Locate(@"ffmpeg7", directory.Root, directory.Combine(@"empty"), [directory.Combine(@"platform")]);

		await Assert.That(located).IsEqualTo(expected);
	}

	[Test]
	public async Task PreferredCommandNameInPathWinsOverPlatformDirectoriesAsync()
	{
		using TestDirectory directory = new();
		string expected = directory.CreateFile(Path.Combine(@"bin", CommandFile));
		directory.CreateFile(Path.Combine(@"platform", CommandFile));

		string? located = FFmpegRunner.Locate(@"ffmpeg7", directory.Root, directory.Combine(@"bin"), [directory.Combine(@"platform")]);

		await Assert.That(located).IsEqualTo(expected);
	}

	[Test]
	public async Task ApplicationDirectoryWinsOverPathAsync()
	{
		using TestDirectory directory = new();
		string expected = directory.CreateFile(Path.Combine(@"app", ExecutableName));
		directory.CreateFile(Path.Combine(@"bin", ExecutableName));

		string? located = FFmpegRunner.Locate(null, directory.Combine(@"app"), directory.Combine(@"bin"), []);

		await Assert.That(located).IsEqualTo(expected);
	}

	[Test]
	public async Task PathWinsOverPlatformDirectoriesAsync()
	{
		using TestDirectory directory = new();
		string expected = directory.CreateFile(Path.Combine(@"bin", ExecutableName));
		directory.CreateFile(Path.Combine(@"platform", ExecutableName));
		string pathVariable = JoinPath(@"", @"  ", directory.Combine(@"nothing"), $"\"{directory.Combine(@"bin")}\"");

		string? located = FFmpegRunner.Locate(@" ", directory.Combine(@"app"), pathVariable, [directory.Combine(@"platform")]);

		await Assert.That(located).IsEqualTo(expected);
	}

	[Test]
	public async Task RelativePathEntriesAreIgnoredAsync()
	{
		using TestDirectory directory = new();
		string expected = directory.CreateFile(Path.Combine(@"platform", ExecutableName));
		directory.CreateFile(Path.Combine(@"bin", ExecutableName));
		string relative = Path.GetRelativePath(Environment.CurrentDirectory, directory.Combine(@"bin"));
		Skip.When(Path.IsPathRooted(relative), @"The temporary directory is on another drive.");

		string? located = FFmpegRunner.Locate(null, directory.Combine(@"app"), relative, [directory.Combine(@"platform")]);

		await Assert.That(located).IsEqualTo(expected);
	}

	[Test]
	public async Task PlatformDirectoriesAreTheLastResortAsync()
	{
		using TestDirectory directory = new();
		string expected = directory.CreateFile(Path.Combine(@"platform2", ExecutableName));

		string? located = FFmpegRunner.Locate(null, directory.Combine(@"app"), null, [directory.Combine(@"platform1"), directory.Combine(@"platform2")]);

		await Assert.That(located).IsEqualTo(expected);
	}

	[Test]
	public async Task DirectoryNamedLikeExecutableIsIgnoredAsync()
	{
		using TestDirectory directory = new();
		Directory.CreateDirectory(directory.Combine(@"app", ExecutableName));

		string? located = FFmpegRunner.Locate(null, directory.Combine(@"app"), null, []);

		await Assert.That(located).IsNull();
	}
}
