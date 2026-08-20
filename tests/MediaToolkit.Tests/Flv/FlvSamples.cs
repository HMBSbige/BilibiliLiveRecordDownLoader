namespace MediaToolkit.Tests.Flv;

internal static class FlvSamples
{
	public static readonly byte[] Sps = Convert.FromHexString(@"6764001FACD9");
	public static readonly byte[] Pps = Convert.FromHexString(@"68EBE3CB");
	public static readonly byte[] IdrSlice = Convert.FromHexString(@"6588840033");
	public static readonly byte[] Sei = Convert.FromHexString(@"060501");

	// 关键帧 + AVC，AVCPacketType 0，CompositionTime 0；AVCDecoderConfigurationRecord 的 lengthSizeMinusOne 为 3，1 个 SPS、1 个 PPS
	public static readonly byte[] AvcSequenceHeader = Convert.FromHexString(@"1700000000" + @"0164001FFF" + @"E10006" + @"6764001FACD9" + @"010004" + @"68EBE3CB");

	public static readonly byte[] AvcKeyFrame = Convert.FromHexString(@"1701000000" + @"00000005" + @"6588840033" + @"00000003" + @"060501");

	// AAC、44.1 kHz、16 bit、立体声；AudioSpecificConfig：AAC LC、采样率索引 4、2 声道
	public static readonly byte[] AacSequenceHeader = Convert.FromHexString(@"AF00" + @"1210");

	public static readonly byte[] AacFrame = Convert.FromHexString(@"AF01" + @"21004990");

	// AAC LC、采样率索引 4、2 声道，帧长 7 + 4 = 11
	public static readonly byte[] AdtsHeaderForAacFrame = Convert.FromHexString(@"FFF1508001" + @"7FFC");

	public static byte[] AnnexB(params byte[][] nalUnits)
	{
		return [.. nalUnits.SelectMany(static nalUnit => (byte[])[0, 0, 0, 1, .. nalUnit])];
	}
}
