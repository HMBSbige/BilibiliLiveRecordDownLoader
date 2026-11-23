namespace MediaToolkit.Flv;

internal enum FlvTagConversion
{
	Frame,

	// 参数集、信息帧、空帧和个别损坏帧，流照常继续
	NonFrame,

	Unsupported
}
