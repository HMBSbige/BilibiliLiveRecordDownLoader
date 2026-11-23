using BilibiliLiveRecordDownLoader.Application.Contracts.Storage;
using BilibiliLiveRecordDownLoader.Application.Storage;
using OS = TUnit.Core.Enums.OS;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Application;

public sealed class DiskSpaceServiceTest
{
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

	// 录播目录可能经符号链接放到别的分区上；/dev 在 Linux 和 macOS 上都单独挂载，和临时目录不在一个分区
	[Test]
	[ExcludeOn(OS.Windows)]
	public async Task SymlinkedRecordDirectoryReportsTheVolumeItPointsToAsync()
	{
		using TestDirectory directory = new();
		string link = directory.Combine("link");
		Directory.CreateSymbolicLink(link, "/dev");

		DiskSpaceInfo? linked = new DiskSpaceService().GetDiskSpace(Path.Combine(link, "records"));

		await Assert.That(linked?.TotalBytes).IsEqualTo(new DriveInfo("/dev").TotalSize);
	}

	[Test]
	public async Task BlankPathHasNoDiskSpaceAsync()
	{
		await Assert.That(new DiskSpaceService().GetDiskSpace("   ")).IsNull();
	}
}
