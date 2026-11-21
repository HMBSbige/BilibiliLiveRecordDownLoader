using BilibiliLiveRecordDownLoader.Application.Contracts.Storage;
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

	[Test]
	public async Task RecordDirectoryThatDoesNotExistYetReportsTheVolumeOfItsParentAsync()
	{
		using TestDirectory directory = new();
		DiskSpaceService service = new();

		DiskSpaceInfo? existing = service.GetDiskSpace(directory.Root);
		DiskSpaceInfo? missing = service.GetDiskSpace(directory.Combine(Path.Combine("not", "created", "yet")));

		using (Assert.Multiple())
		{
			await Assert.That(existing is { AvailableBytes: > 0 } info && info.TotalBytes >= info.AvailableBytes).IsTrue();
			await Assert.That(missing?.TotalBytes).IsEqualTo(existing?.TotalBytes);
		}
	}

	[Test]
	[Arguments("")]
	[Arguments("   ")]
	public async Task BlankPathHasNoDiskSpaceAsync(string path)
	{
		await Assert.That(new DiskSpaceService().GetDiskSpace(path)).IsNull();
	}
}
