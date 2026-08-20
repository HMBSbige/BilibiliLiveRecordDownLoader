namespace MediaToolkit.Flv;

[Flags]
internal enum FlvHeaderFlags : byte
{
	Video = 0x01,
	Audio = 0x04
}
