using BilibiliLiveRecordDownLoader.Domain.Shared.Localization;

namespace BilibiliLiveRecordDownLoader.Application.Contracts.Accounts;

public sealed class LoginRequiredException(Exception? innerException = null) : Exception(AppLanguage.Current.LoginRequired, innerException);
