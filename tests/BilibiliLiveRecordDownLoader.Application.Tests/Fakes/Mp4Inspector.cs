using System.Buffers.Binary;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

internal sealed record TopBox(string Type, long Offset, long Size);

internal sealed record EditListEntry(long Duration, long MediaTime);

// 一条轨道的样本表；Samples 是按样本表算出的每个样本在文件里的位置
// Duration 和 Flags 来自 tkhd
internal sealed record TrackTable(uint TrackId, string Handler, uint Timescale, long Duration, uint Flags, long MediaDuration, EditListEntry[] Edits, (uint Count, uint Delta)[] TimeToSample, (uint Count, int Offset)[] CompositionOffsets, uint[]? SyncSamples, (long Offset, uint Size)[] Samples, bool HasLongOffsets, uint? ChapterTrackId);

// 测试里的文件都不大，整个读进内存解析
internal static class Mp4Inspector
{
	public static TopBox[] ReadTopLevel(byte[] file)
	{
		List<TopBox> boxes = [];
		long offset = 0;

		while (offset + 8 <= file.Length)
		{
			long size = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan((int)offset));

			if (size is 1)
			{
				size = (long)BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan((int)offset + 8));
			}

			boxes.Add(new TopBox(Encoding.ASCII.GetString(file, (int)offset + 4, 4), offset, size));

			if (size < 8)
			{
				break;
			}

			offset += size;
		}

		return [.. boxes];
	}

	public static byte[] ReadMovie(byte[] file)
	{
		TopBox moov = ReadTopLevel(file).Last(static box => box.Type is "moov");
		return file.AsSpan((int)moov.Offset + 8, (int)moov.Size - 8).ToArray();
	}

	public static (uint Timescale, long Duration) ReadMovieHeader(byte[] file)
	{
		byte[] mvhd = Get(ReadMovie(file), "mvhd");
		return mvhd[0] is 1 ? (BinaryPrimitives.ReadUInt32BigEndian(mvhd.AsSpan(20)), (long)BinaryPrimitives.ReadUInt64BigEndian(mvhd.AsSpan(24))) : (BinaryPrimitives.ReadUInt32BigEndian(mvhd.AsSpan(12)), BinaryPrimitives.ReadUInt32BigEndian(mvhd.AsSpan(16)));
	}

	public static TrackTable[] ReadTracks(byte[] file)
	{
		return ReadTracksFromMovie(ReadMovie(file));
	}

	public static TrackTable[] ReadTracksFromMovie(byte[] movie)
	{
		return [.. Children(movie, "trak").Select(ReadTrack)];
	}

	public static byte[]? Find(byte[] data, params ReadOnlySpan<string> path)
	{
		byte[]? current = data;

		foreach (string type in path)
		{
			current = current is null ? null : Children(current, type).FirstOrDefault();
		}

		return current;
	}

	public static byte[] Get(byte[] data, params ReadOnlySpan<string> path)
	{
		return Find(data, path) ?? throw new InvalidDataException($"no {string.Join('/', path)}");
	}

	public static IEnumerable<byte[]> Children(byte[] data, string type)
	{
		int offset = 0;

		while (offset + 8 <= data.Length)
		{
			int size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

			if (size < 8 || offset + size > data.Length)
			{
				yield break;
			}

			if (Encoding.ASCII.GetString(data, offset + 4, 4) == type)
			{
				yield return data.AsSpan(offset + 8, size - 8).ToArray();
			}

			offset += size;
		}
	}

	private static TrackTable ReadTrack(byte[] trak)
	{
		byte[] tkhd = Get(trak, "tkhd");
		uint trackId = BinaryPrimitives.ReadUInt32BigEndian(tkhd.AsSpan(tkhd[0] is 1 ? 20 : 12));
		long duration = tkhd[0] is 1 ? (long)BinaryPrimitives.ReadUInt64BigEndian(tkhd.AsSpan(28)) : BinaryPrimitives.ReadUInt32BigEndian(tkhd.AsSpan(20));
		uint flags = BinaryPrimitives.ReadUInt32BigEndian(tkhd) & 0x00FF_FFFF;
		byte[] mdhd = Get(trak, "mdia", "mdhd");
		uint timescale = BinaryPrimitives.ReadUInt32BigEndian(mdhd.AsSpan(mdhd[0] is 1 ? 20 : 12));
		long mediaDuration = mdhd[0] is 1 ? (long)BinaryPrimitives.ReadUInt64BigEndian(mdhd.AsSpan(24)) : BinaryPrimitives.ReadUInt32BigEndian(mdhd.AsSpan(16));
		string handler = Encoding.ASCII.GetString(Get(trak, "mdia", "hdlr"), 8, 4);
		byte[] stbl = Get(trak, "mdia", "minf", "stbl");

		byte[] stsz = Get(stbl, "stsz");
		uint uniform = BinaryPrimitives.ReadUInt32BigEndian(stsz.AsSpan(4));
		uint[] sizes = [.. Enumerable.Range(0, (int)BinaryPrimitives.ReadUInt32BigEndian(stsz.AsSpan(8))).Select(index => uniform is 0 ? BinaryPrimitives.ReadUInt32BigEndian(stsz.AsSpan(12 + 4 * index)) : uniform)];

		byte[]? co64 = Find(stbl, "co64");
		byte[] chunkTable = co64 ?? Get(stbl, "stco");
		long[] chunks = [.. Enumerable.Range(0, (int)BinaryPrimitives.ReadUInt32BigEndian(chunkTable.AsSpan(4))).Select(index => co64 is null ? BinaryPrimitives.ReadUInt32BigEndian(chunkTable.AsSpan(8 + 4 * index)) : (long)BinaryPrimitives.ReadUInt64BigEndian(chunkTable.AsSpan(8 + 8 * index)))];

		byte[] stsc = Get(stbl, "stsc");
		(uint First, uint PerChunk)[] runs = [.. Enumerable.Range(0, (int)BinaryPrimitives.ReadUInt32BigEndian(stsc.AsSpan(4))).Select(index => (BinaryPrimitives.ReadUInt32BigEndian(stsc.AsSpan(8 + 12 * index)), BinaryPrimitives.ReadUInt32BigEndian(stsc.AsSpan(12 + 12 * index))))];
		List<(long, uint)> samples = [];
		int sample = 0;

		for (int chunk = 0; chunk < chunks.Length; ++chunk)
		{
			uint perChunk = runs.Last(run => run.First <= chunk + 1).PerChunk;
			long position = chunks[chunk];

			for (uint i = 0; i < perChunk; ++i, ++sample)
			{
				samples.Add((position, sizes[sample]));
				position += sizes[sample];
			}
		}

		return new TrackTable(trackId, handler, timescale, duration, flags, mediaDuration, ReadEdits(trak), ReadPairs(stbl, "stts"), [.. ReadPairs(stbl, "ctts").Select(static pair => (pair.Count, (int)pair.Delta))], Find(stbl, "stss") is { } stss ? [.. Enumerable.Range(0, (int)BinaryPrimitives.ReadUInt32BigEndian(stss.AsSpan(4))).Select(index => BinaryPrimitives.ReadUInt32BigEndian(stss.AsSpan(8 + 4 * index)))] : null, [.. samples], co64 is not null, Find(trak, "tref", "chap") is { } chap ? BinaryPrimitives.ReadUInt32BigEndian(chap) : null);
	}

	private static EditListEntry[] ReadEdits(byte[] trak)
	{
		if (Find(trak, "edts", "elst") is not { } elst)
		{
			return [];
		}

		bool isLong = elst[0] is 1;
		int size = isLong ? 20 : 12;
		return [.. Enumerable.Range(0, (int)BinaryPrimitives.ReadUInt32BigEndian(elst.AsSpan(4))).Select(index => isLong
			? new EditListEntry((long)BinaryPrimitives.ReadUInt64BigEndian(elst.AsSpan(8 + size * index)), BinaryPrimitives.ReadInt64BigEndian(elst.AsSpan(16 + size * index)))
			: new EditListEntry(BinaryPrimitives.ReadUInt32BigEndian(elst.AsSpan(8 + size * index)), BinaryPrimitives.ReadInt32BigEndian(elst.AsSpan(12 + size * index))))];
	}

	private static (uint Count, uint Delta)[] ReadPairs(byte[] stbl, string type)
	{
		return Find(stbl, type) is { } table ? [.. Enumerable.Range(0, (int)BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(4))).Select(index => (BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(8 + 8 * index)), BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(12 + 8 * index))))] : [];
	}
}
