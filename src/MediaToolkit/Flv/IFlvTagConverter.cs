using System.Buffers;

namespace MediaToolkit.Flv;

internal interface IFlvTagConverter
{
	// data 是整个 tag 的数据部分，从 audio/video tag header 的第一个字节开始
	FlvTagConversion Convert(ReadOnlySpan<byte> data, IBufferWriter<byte> output);
}
