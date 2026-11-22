using System.Buffers;

namespace MediaToolkit.Flv;

internal interface IFlvTagConverter
{
	/// <param name="data">整个 tag 的数据部分，从 audio/video tag header 的第一个字节开始</param>
	FlvTagConversion Convert(ReadOnlySpan<byte> data, IBufferWriter<byte> output);
}
