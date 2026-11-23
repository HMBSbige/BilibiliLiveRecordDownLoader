namespace MediaToolkit.Flv;

[Flags]
internal enum FlvHeaderFlags : byte
{
	None = 0,
	Video = 0x01,
	Audio = 0x04
}
