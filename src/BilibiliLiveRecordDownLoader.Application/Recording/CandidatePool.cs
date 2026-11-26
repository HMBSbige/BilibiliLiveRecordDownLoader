using System.Runtime.CompilerServices;

namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 断流重连时继续用已经拿到的签名地址：重新请求可能分到另一路流，比如二压转码流
// 不按地址里的有效期提前作废：各节点的有效期参数不同，有的不校验；签名作废后 CDN 很快返回 403（HLS 打开时就取第一个分片），等它失败再重新请求只多一轮打开失败
internal sealed partial class CandidatePool(ILogger logger)
{
	// FastestResponse 下各主机的打开失败回调会并发；其余调用都在一路打开结束之后
	private readonly Lock _lock = new();
	private readonly HashSet<StreamEndpoint> _failedEndpoints = [];
	private readonly HashSet<StreamEndpoint> _rejectedBeforeVerified = [];
	private CandidateBatch? _batch;

	// 录满过说明这批地址能用，之后全部打不开多半是签名作废，立即重新请求；没录满过多半是流本身有问题（接口仍显示直播而各节点 404），重新请求也一样，要等重连间隔
	private bool _isVerified;

	// 403 多半是签名作废，验证之前就被拒的地址从来没能用过，不算；其他节点不校验时靠后的流还能录，不重新请求就回不到首选流：整路打不开就立即重新请求，从别的主机打开了就录完这次再请求
	private bool _isRejected;

	public async IAsyncEnumerable<(CandidateBatch Batch, LiveStreamCandidate Candidate)> GetCandidatesAsync(CandidateRequest request, Func<CancellationToken, Task<CandidateBatch>> fetch, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		if (_batch?.Request != request)
		{
			Discard();
		}

		while (true)
		{
			CandidateBatch batch = _batch ?? Adopt(await fetch(cancellationToken));

			foreach (LiveStreamCandidate offered in batch.Candidates)
			{
				if (WithoutFailedHosts(offered) is not { } candidate)
				{
					continue;
				}

				yield return (batch, candidate);

				// 回到这里说明这一路没打开
				if (_isRejected)
				{
					break;
				}
			}

			if (!RetireBatch())
			{
				yield break;
			}
		}
	}

	public void MarkFailed(LiveStreamCandidate candidate, Uri endpoint, bool isRejected)
	{
		StreamEndpoint failed = StreamEndpoint.From(candidate, endpoint);

		lock (_lock)
		{
			_failedEndpoints.Add(failed);

			if (isRejected)
			{
				if (_isVerified)
				{
					_isRejected |= !_rejectedBeforeVerified.Contains(failed);
				}
				else
				{
					_rejectedBeforeVerified.Add(failed);
				}
			}
		}
	}

	public AttemptVerdict OnAttemptEnded(RecordingAttempt attempt)
	{
		if (!attempt.IsQuickFailure)
		{
			// 之前失败的线路可能已经恢复，回到首选流
			_failedEndpoints.Clear();

			if (attempt.Restriction is not null || _isRejected)
			{
				// 重新请求才有机会拿到更高画质，被拒的地址也要重新签名
				Discard();
			}
			else if (attempt.IsSustained)
			{
				_isVerified = true;
			}

			return AttemptVerdict.Reconnect;
		}

		// 写盘失败不怪线路
		if (attempt.End is RecordingEnd.WriteFailed)
		{
			return AttemptVerdict.Reconnect;
		}

		_failedEndpoints.Add(StreamEndpoint.From(attempt.Candidate, attempt.Endpoint));

		if (_batch is not { } batch || HasUntriedCandidate(batch))
		{
			return AttemptVerdict.Reconnect;
		}

		return RetireBatch() ? AttemptVerdict.Reconnect : AttemptVerdict.WaitBeforeReconnect;
	}

	private CandidateBatch Adopt(CandidateBatch batch)
	{
		_batch = batch;

		// 失败标记来自请求变化前的一批，新的一批全被标过时按之前失败的线路已经恢复处理，从首选流试起
		if (!HasUntriedCandidate(batch))
		{
			_failedEndpoints.Clear();
		}

		return batch;
	}

	// 之前失败的线路可能已经恢复，下一批从首选流试起
	private bool RetireBatch()
	{
		bool isVerified = _isVerified;
		_failedEndpoints.Clear();
		Discard();

		if (isVerified)
		{
			LogVerifiedBatchFailed(logger);
		}

		return isVerified;
	}

	private void Discard()
	{
		_batch = null;
		_isVerified = false;
		_isRejected = false;
		_rejectedBeforeVerified.Clear();
	}

	private LiveStreamCandidate? WithoutFailedHosts(LiveStreamCandidate candidate)
	{
		if (_failedEndpoints.Count is 0 || candidate.Urls.All(uri => IsUntried(candidate, uri)))
		{
			return candidate;
		}

		Uri[] untried = [.. candidate.Urls.Where(uri => IsUntried(candidate, uri))];
		return untried is [] ? null : candidate with { Urls = untried };
	}

	private bool HasUntriedCandidate(CandidateBatch batch)
	{
		return batch.Candidates.Any(candidate => candidate.Urls.Any(uri => IsUntried(candidate, uri)));
	}

	private bool IsUntried(LiveStreamCandidate candidate, Uri uri)
	{
		return !_failedEndpoints.Contains(StreamEndpoint.From(candidate, uri));
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "The stream URLs that recorded before stopped working, requesting new ones")]
	private static partial void LogVerifiedBatchFailed(ILogger logger);

	// 重新请求后签名会变，只用主机区分线路；变体和接口给的流、不同档位的变体都在同一主机上，要分开记
	private readonly record struct StreamEndpoint(StreamProtocol Protocol, StreamFormat Format, StreamCodec Codec, long? VariantQn, string Authority)
	{
		public static StreamEndpoint From(LiveStreamCandidate candidate, Uri uri)
		{
			return new StreamEndpoint(candidate.Protocol, candidate.Format, candidate.Codec, (candidate as LiveStreamCandidate.Variant)?.Qn, uri.Authority);
		}
	}
}

internal enum AttemptVerdict
{
	Reconnect,
	WaitBeforeReconnect
}
