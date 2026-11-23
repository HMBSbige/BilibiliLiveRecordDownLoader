using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace BilibiliLiveRecordDownLoader.Views.Services;

internal sealed class SystemLanguageService : ISystemLanguageService
{
	public IObservable<string> PreferredLanguage { get; } = Signal.Defer(static () => Avalonia.Application.Current?.PlatformSettings is { } settings
		? Signal.FromEventPattern(handler => settings.PreferredApplicationLanguageChanged += handler, handler => settings.PreferredApplicationLanguageChanged -= handler)
			.Map(_ => settings.PreferredApplicationLanguage)
			.StartWith(settings.PreferredApplicationLanguage)
		: Signal.Return(string.Empty));
}
