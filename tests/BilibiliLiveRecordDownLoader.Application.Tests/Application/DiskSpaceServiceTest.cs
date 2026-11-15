using BilibiliLiveRecordDownLoader.Application.Storage;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class DiskSpaceServiceTest
{
	[Test]
	[Arguments("/mnt/data/videos", "/mnt/data", true)]
	[Arguments("/mnt/data", "/mnt/data", true)]
	[Arguments("/mnt/data2/videos", "/mnt/data", false)]
	[Arguments("/mnt/data2/videos", "/", true)]
	public async Task MountPointMatchesOnlyOnDirectoryBoundaryAsync(string path, string root, bool expected)
	{
		await Assert.That(DiskSpaceService.IsUnder(path, root, StringComparison.Ordinal)).IsEqualTo(expected);
	}
}
