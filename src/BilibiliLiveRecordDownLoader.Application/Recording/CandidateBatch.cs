namespace BilibiliLiveRecordDownLoader.Application.Recording;

// 同一次请求拿到的候选流和请求时本地有没有配 Cookie；判断受限原因要按请求时的身份，之后登录或退出不影响这批流
internal sealed record CandidateBatch(IReadOnlyList<LiveStreamCandidate> Candidates, bool HasCookie);
