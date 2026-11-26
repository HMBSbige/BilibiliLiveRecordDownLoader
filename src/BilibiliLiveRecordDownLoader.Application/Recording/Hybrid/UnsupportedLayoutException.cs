namespace BilibiliLiveRecordDownLoader.Application.Recording.Hybrid;

// 消息是给日志和通知看的技术细节，不翻译
internal sealed class UnsupportedLayoutException(string message) : Exception(message);
