namespace MediaToolkit.FFmpeg;

// 填充数据的 NAL 类型按编码不同：H.264 是 12，HEVC 是 38
public enum FillerRemoval
{
	None,
	Avc,
	Hevc
}
