namespace BilibiliLiveRecordDownLoader.FlvProcessor.Models;

public sealed record FractionUInt32
{
	public uint N { get; private set; }
	public uint D { get; private set; }

	public FractionUInt32(uint n, uint d)
	{
		N = n;
		D = d;
		Reduce();
	}

	private static uint Gcd(uint left, uint right)
	{
		// Executes the classic Euclidean algorithm.

		// https://en.wikipedia.org/wiki/Euclidean_algorithm

		while (right != 0)
		{
			var temp = left % right;
			left = right;
			right = temp;
		}

		return left;
	}

	private double ToDouble()
	{
		return (double)N / D;
	}

	private void Reduce()
	{
		var gcd = Gcd(N, D);
		N /= gcd;
		D /= gcd;
	}

	public override string ToString()
	{
		return $@"{ToDouble()} ({N}/{D})";
	}
}
