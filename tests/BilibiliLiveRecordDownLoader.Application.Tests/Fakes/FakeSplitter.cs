using BilibiliLiveRecordDownLoader.Application.Recording.Splitting;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Tests.Fakes;

/// <summary>
/// 元素是 [类型][长度][标记][填充]：K 是切分点，S 是序列头，D 是普通数据。新段的头是 "H" 加上已经过去的序列头个数和冒号。填充里的 F 是可以去掉的填充数据。
/// </summary>
internal sealed class FakeSplitter(bool holdsFirstUnit = false) : MediaSplitter
{
	private int _sequences;

	public override bool HoldsFirstUnit => holdsFirstUnit;

	public static byte[] Unit(string marker, int length, int filler = 0)
	{
		byte[] unit = Element('K', marker, length);
		unit.AsSpan(length - filler).Fill((byte)'F');
		return unit;
	}

	public static byte[] Data(string marker, int length)
	{
		return Element('D', marker, length);
	}

	public static byte[] Sequence(string marker, int length)
	{
		return Element('S', marker, length);
	}

	public static string Describe(IEnumerable<byte[]> files)
	{
		return string.Join(" | ", files.Select(Describe));
	}

	public static string Describe(byte[] file)
	{
		StringBuilder builder = new();
		ReadOnlySpan<byte> data = file;

		while (!data.IsEmpty)
		{
			if (data[0] is (byte)'H')
			{
				int end = data.IndexOf((byte)':') + 1;
				builder.Append(Encoding.ASCII.GetString(data.Slice(0, end)));
				data = data.Slice(end);
				continue;
			}

			builder.Append((char)data[2]);
			data = data.Slice(Math.Min(data[1], data.Length));
		}

		return builder.ToString();
	}

	public override int RemoveFiller(Span<byte> elements, bool isEnabled)
	{
		int read = 0;
		int write = 0;

		while (isEnabled && elements.Length - read >= 2 && elements[read + 1] <= elements.Length - read)
		{
			int length = elements[read + 1];
			int start = write;
			elements.Slice(read, 3).CopyTo(elements.Slice(write));
			write += 3;

			foreach (byte value in elements.Slice(read + 3, length - 3))
			{
				if (value is not (byte)'F')
				{
					elements[write++] = value;
				}
			}

			elements[start + 1] = (byte)(write - start);
			read += length;
		}

		elements.Slice(read).CopyTo(elements.Slice(write));
		return write + elements.Length - read;
	}

	protected override int Measure(ReadOnlySpan<byte> data, out bool isSplit)
	{
		isSplit = data is [(byte)'K', ..];
		return data.Length < 2 ? 0 : data[1];
	}

	protected override void Accept(ReadOnlySpan<byte> element)
	{
		if (element[0] is (byte)'S')
		{
			++_sequences;
		}
	}

	protected override ReadOnlyMemory<byte> CaptureHeader()
	{
		return Encoding.ASCII.GetBytes($"H{_sequences}:");
	}

	private static byte[] Element(char type, string marker, int length)
	{
		byte[] element = new byte[length];
		element.AsSpan().Fill((byte)'.');
		element[0] = (byte)type;
		element[1] = (byte)length;
		element[2] = (byte)marker[0];
		return element;
	}
}
