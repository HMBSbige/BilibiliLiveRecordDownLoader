using BilibiliLiveRecordDownLoader.Application.Contracts.Storage;
using BilibiliLiveRecordDownLoader.Application.Storage;
using TUnit.Assertions.Enums;

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
	[Arguments(true, "/mnt/data")]
	[Arguments(false, null)]
	public async Task OnlyTheLongestMatchingMountIsProbedAsync(bool dataReady, string? expected)
	{
		(string Root, bool IsReady)[] mounts = [("/", true), ("/mnt/net", false), ("/mnt/data", dataReady)];
		List<string> probed = [];

		string? mount = DiskSpaceService.FindReadyMount(mounts.Select(static mount => mount.Root), static root => root, root =>
		{
			probed.Add(root);
			return mounts.Single(mount => mount.Root == root).IsReady;
		}, "/mnt/data/videos", StringComparison.Ordinal);

		using (Assert.Multiple())
		{
			await Assert.That(mount).IsEqualTo(expected);
			await Assert.That(probed).IsEquivalentTo(["/mnt/data"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task RecordDirectoryThatDoesNotExistYetReportsTheVolumeOfItsParentAsync()
	{
		using TestDirectory directory = new();
		DiskSpaceService service = new();

		DiskSpaceInfo existing = await Assert.That(service.GetDiskSpace(directory.Root)).IsNotNull();
		DiskSpaceInfo? missing = service.GetDiskSpace(directory.Combine(Path.Combine("not", "created", "yet")));

		using (Assert.Multiple())
		{
			await Assert.That(existing.AvailableBytes).IsGreaterThan(0).And.IsLessThanOrEqualTo(existing.TotalBytes);
			await Assert.That(missing?.TotalBytes).IsEqualTo(existing.TotalBytes);
		}
	}

	[Test]
	public async Task BlankPathHasNoDiskSpaceAsync()
	{
		await Assert.That(new DiskSpaceService().GetDiskSpace("   ")).IsNull();
	}
}
