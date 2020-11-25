using BilibiliLiveRecordDownLoader.FFmpeg;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

namespace UnitTest
{
	[TestClass]
	public class FFmpegTest
	{
		[TestMethod]
		public async Task VerifyTestAsync()
		{
			using var ffmpeg = new FFmpegCommand
			{
				FFmpegPath = @"ffmpeg"
			};
			using var _ = ffmpeg.MessageUpdated.Subscribe(Console.WriteLine);
			Assert.IsTrue(await ffmpeg.VerifyAsync(default));
		}

		[TestMethod]
		public async Task CommandTestAsync()
		{
			using var ffmpeg = new FFmpegCommand
			{
				FFmpegPath = @"ffmpeg"
			};
			using var _ = ffmpeg.MessageUpdated.Subscribe(Console.WriteLine);
			await ffmpeg.StartAsync(@"-h", default);
		}
	}
}
