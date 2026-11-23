using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels.Platform;
using BilibiliLiveRecordDownLoader.ViewModels.Settings;
using ReactiveUI.Primitives.Signals;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class AppearanceSettingsViewModelTest
{
	[Test]
	[Arguments("EN", "en")]
	[Arguments("zh-hans", "zh-Hans")]
	[Arguments("zh-CN", "")]
	[Arguments(null, "")]
	public async Task ConfiguredLanguageIsMatchedIgnoringCaseAsync(string? configured, string expected)
	{
		using AppearanceSettingsViewModel viewModel = new(new SettingsAppService(new FakeConfigManager()), new SystemStub(), new SystemStub());

		await Assert.That(viewModel.FindLanguage(configured).CultureName).IsEqualTo(expected);
	}

	private sealed class SystemStub : IThemeService, ISystemLanguageService
	{
		public IObservable<string> PreferredLanguage { get; } = Signal.Return(string.Empty);

		public void Apply(AppTheme theme)
		{
		}
	}
}
