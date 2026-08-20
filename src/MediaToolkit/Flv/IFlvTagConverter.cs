using System.Buffers;

namespace MediaToolkit.Flv;

internal interface IFlvTagConverter
{
	/// <param name="data">整个 tag 的数据部分，从 audio/video tag header 的第一个字节开始</param>
	/// <param name="output">不支持的编码或无法解析的数据不写入任何内容</param>
	void Convert(ReadOnlySpan<byte> data, IBufferWriter<byte> output);
}
