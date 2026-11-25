using System.Buffers.Binary;
using System.Text;

namespace BilibiliLiveRecordDownLoader.Application.Recording.Containers;

// 盒子类型按大端读成整数比较
internal static class BoxType
{
	public const uint Ftyp = 'f' << 24 | 't' << 16 | 'y' << 8 | 'p';
	public const uint Moov = 'm' << 24 | 'o' << 16 | 'o' << 8 | 'v';
	public const uint Moof = 'm' << 24 | 'o' << 16 | 'o' << 8 | 'f';
	public const uint Mdat = 'm' << 24 | 'd' << 16 | 'a' << 8 | 't';
	public const uint Free = 'f' << 24 | 'r' << 16 | 'e' << 8 | 'e';
	public const uint Uuid = 'u' << 24 | 'u' << 16 | 'i' << 8 | 'd';
	public const uint Sidx = 's' << 24 | 'i' << 16 | 'd' << 8 | 'x';
	public const uint Pssh = 'p' << 24 | 's' << 16 | 's' << 8 | 'h';
	public const uint Mvhd = 'm' << 24 | 'v' << 16 | 'h' << 8 | 'd';
	public const uint Mvex = 'm' << 24 | 'v' << 16 | 'e' << 8 | 'x';
	public const uint Trex = 't' << 24 | 'r' << 16 | 'e' << 8 | 'x';
	public const uint Udta = 'u' << 24 | 'd' << 16 | 't' << 8 | 'a';
	public const uint Chpl = 'c' << 24 | 'h' << 16 | 'p' << 8 | 'l';
	public const uint Trak = 't' << 24 | 'r' << 16 | 'a' << 8 | 'k';
	public const uint Tkhd = 't' << 24 | 'k' << 16 | 'h' << 8 | 'd';
	public const uint Edts = 'e' << 24 | 'd' << 16 | 't' << 8 | 's';
	public const uint Elst = 'e' << 24 | 'l' << 16 | 's' << 8 | 't';
	public const uint Tref = 't' << 24 | 'r' << 16 | 'e' << 8 | 'f';
	public const uint Mdia = 'm' << 24 | 'd' << 16 | 'i' << 8 | 'a';
	public const uint Mdhd = 'm' << 24 | 'd' << 16 | 'h' << 8 | 'd';
	public const uint Hdlr = 'h' << 24 | 'd' << 16 | 'l' << 8 | 'r';
	public const uint Minf = 'm' << 24 | 'i' << 16 | 'n' << 8 | 'f';
	public const uint Gmhd = 'g' << 24 | 'm' << 16 | 'h' << 8 | 'd';
	public const uint Gmin = 'g' << 24 | 'm' << 16 | 'i' << 8 | 'n';
	public const uint Dinf = 'd' << 24 | 'i' << 16 | 'n' << 8 | 'f';
	public const uint Dref = 'd' << 24 | 'r' << 16 | 'e' << 8 | 'f';
	public const uint Url = 'u' << 24 | 'r' << 16 | 'l' << 8 | ' ';
	public const uint Stbl = 's' << 24 | 't' << 16 | 'b' << 8 | 'l';
	public const uint Stsd = 's' << 24 | 't' << 16 | 's' << 8 | 'd';
	public const uint Stts = 's' << 24 | 't' << 16 | 't' << 8 | 's';
	public const uint Ctts = 'c' << 24 | 't' << 16 | 't' << 8 | 's';
	public const uint Stss = 's' << 24 | 't' << 16 | 's' << 8 | 's';
	public const uint Stsc = 's' << 24 | 't' << 16 | 's' << 8 | 'c';
	public const uint Stsz = 's' << 24 | 't' << 16 | 's' << 8 | 'z';
	public const uint Stco = 's' << 24 | 't' << 16 | 'c' << 8 | 'o';
	public const uint Co64 = 'c' << 24 | 'o' << 16 | '6' << 8 | '4';
	public const uint Sgpd = 's' << 24 | 'g' << 16 | 'p' << 8 | 'd';
	public const uint Sbgp = 's' << 24 | 'b' << 16 | 'g' << 8 | 'p';
	public const uint Mfhd = 'm' << 24 | 'f' << 16 | 'h' << 8 | 'd';
	public const uint Traf = 't' << 24 | 'r' << 16 | 'a' << 8 | 'f';
	public const uint Tfhd = 't' << 24 | 'f' << 16 | 'h' << 8 | 'd';
	public const uint Tfdt = 't' << 24 | 'f' << 16 | 'd' << 8 | 't';
	public const uint Trun = 't' << 24 | 'r' << 16 | 'u' << 8 | 'n';
	public const uint Senc = 's' << 24 | 'e' << 16 | 'n' << 8 | 'c';
	public const uint Saiz = 's' << 24 | 'a' << 16 | 'i' << 8 | 'z';
	public const uint Saio = 's' << 24 | 'a' << 16 | 'i' << 8 | 'o';
	public const uint Subs = 's' << 24 | 'u' << 16 | 'b' << 8 | 's';

	// 样本项和其中的解码器配置
	public const uint Avc1 = 'a' << 24 | 'v' << 16 | 'c' << 8 | '1';
	public const uint Avc3 = 'a' << 24 | 'v' << 16 | 'c' << 8 | '3';
	public const uint AvcC = 'a' << 24 | 'v' << 16 | 'c' << 8 | 'C';
	public const uint Hvc1 = 'h' << 24 | 'v' << 16 | 'c' << 8 | '1';
	public const uint Hev1 = 'h' << 24 | 'e' << 16 | 'v' << 8 | '1';
	public const uint HvcC = 'h' << 24 | 'v' << 16 | 'c' << 8 | 'C';
	public const uint Av01 = 'a' << 24 | 'v' << 16 | '0' << 8 | '1';
	public const uint Mp4a = 'm' << 24 | 'p' << 16 | '4' << 8 | 'a';
	public const uint Esds = 'e' << 24 | 's' << 16 | 'd' << 8 | 's';
	public const uint Encv = 'e' << 24 | 'n' << 16 | 'c' << 8 | 'v';
	public const uint Enca = 'e' << 24 | 'n' << 16 | 'c' << 8 | 'a';

	// tref 里的引用类型
	public const uint Chap = 'c' << 24 | 'h' << 16 | 'a' << 8 | 'p';

	// hdlr 里的处理类型，Text 也是章节轨的样本项和 gmhd 里的盒子
	public const uint Vide = 'v' << 24 | 'i' << 16 | 'd' << 8 | 'e';
	public const uint Soun = 's' << 24 | 'o' << 16 | 'u' << 8 | 'n';
	public const uint Text = 't' << 24 | 'e' << 16 | 'x' << 8 | 't';

	// sgpd 和 sbgp 里的样本组类型
	public const uint Roll = 'r' << 24 | 'o' << 16 | 'l' << 8 | 'l';

	public static string GetName(uint type)
	{
		Span<byte> name = stackalloc byte[sizeof(uint)];
		BinaryPrimitives.WriteUInt32BigEndian(name, type);
		return Encoding.ASCII.GetString(name);
	}
}
