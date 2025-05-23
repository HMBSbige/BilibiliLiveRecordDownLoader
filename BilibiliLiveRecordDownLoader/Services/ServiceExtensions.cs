using BilibiliApi.Clients;
using BilibiliApi.StreamUriSelectors;
using BilibiliLiveRecordDownLoader.Enums;
using BilibiliLiveRecordDownLoader.FFmpeg;
using BilibiliLiveRecordDownLoader.FlvProcessor.Clients;
using BilibiliLiveRecordDownLoader.FlvProcessor.Interfaces;
using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Models;
using BilibiliLiveRecordDownLoader.Models.TaskViewModels;
using BilibiliLiveRecordDownLoader.Shared.Utils;
using BilibiliLiveRecordDownLoader.ViewModels;
using BilibiliLiveRecordDownLoader.Views;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Punchclock;
using ReactiveUI;
using RunAtStartup;
using System.Net.Http;

namespace BilibiliLiveRecordDownLoader.Services;

public static class ServiceExtensions
{
	public static IServiceCollection AddViewModels(this IServiceCollection services)
	{
		services.TryAddSingleton<MainWindowViewModel>();
		services.TryAddSingleton<TaskListViewModel>();
		services.TryAddSingleton<LogViewModel>();
		services.TryAddSingleton<SettingViewModel>();
		services.TryAddSingleton<StreamRecordViewModel>();
		services.TryAddSingleton<UserSettingsViewModel>();
		services.TryAddSingleton<FFmpegCommandViewModel>();

		services.TryAddSingleton<IScreen>(provider => provider.GetRequiredService<MainWindowViewModel>());

		return services;
	}

	public static IServiceCollection AddViews(this IServiceCollection services)
	{
		services.TryAddSingleton<MainWindow>();
		services.TryAddTransient<IViewFor<TaskListViewModel>, TaskListView>();
		services.TryAddTransient<IViewFor<LogViewModel>, LogView>();
		services.TryAddTransient<IViewFor<SettingViewModel>, SettingView>();
		services.TryAddTransient<IViewFor<StreamRecordViewModel>, StreamRecordView>();
		services.TryAddTransient<IViewFor<UserSettingsViewModel>, UserSettingsView>();
		services.TryAddTransient<IViewFor<FFmpegCommandViewModel>, FFmpegCommandView>();

		return services;
	}

	public static IServiceCollection AddDanmuClients(this IServiceCollection services)
	{
		services.AddDistributedMemoryCache();
		services.TryAddTransient<TcpDanmuClient>();
		services.TryAddTransient<WsDanmuClient>();
		services.TryAddTransient<WssDanmuClient>();

		return services;
	}

	public static IServiceCollection AddConfig(this IServiceCollection services)
	{
		services.TryAddSingleton<IConfigService, ConfigService>();
		services.TryAddSingleton<Config>();

		return services;
	}

	public static IServiceCollection AddDynamicData(this IServiceCollection services)
	{
		services.TryAddSingleton<SourceList<RoomStatus>>();
		services.TryAddSingleton<SourceList<TaskViewModel>>();

		return services;
	}

	public static IServiceCollection AddFlvProcessor(this IServiceCollection services)
	{
		services.TryAddTransient<IFlvExtractor, FlvExtractor>();
		services.TryAddTransient<FFmpegCommand>();

		return services;
	}

	public static IServiceCollection AddStartupService(this IServiceCollection services)
	{
		services.TryAddSingleton(new StartupService(nameof(BilibiliLiveRecordDownLoader)));

		return services;
	}

	public static IServiceCollection AddGlobalTaskQueue(this IServiceCollection services)
	{
		services.TryAddSingleton(new OperationQueue(int.MaxValue));

		return services;
	}

	public static IServiceCollection AddBilibiliApiClient(this IServiceCollection services)
	{
		services.TryAddSingleton(provider =>
		{
			Config config = provider.GetRequiredService<Config>();
			HttpClient client = HttpClientUtils.BuildClientForBilibili(config.UserAgent ?? Config.DefaultUserAgent, config.Cookie, config.HttpHandler);
			return new BilibiliApiClient(client);
		});

		return services;
	}

	public static IServiceCollection AddRecorder(this IServiceCollection services)
	{
		services.TryAddTransient(provider =>
		{
			Config config = provider.GetRequiredService<Config>();
			HttpClient client = HttpClientUtils.BuildClientForBilibili(config.UserAgent ?? Config.DefaultUserAgent, config.Cookie, config.HttpHandler);
			return new HttpFlvLiveStreamRecorder(client, provider.GetRequiredService<ILogger<HttpFlvLiveStreamRecorder>>());
		});
		services.TryAddTransient(provider =>
		{
			Config config = provider.GetRequiredService<Config>();
			HttpClient client = HttpClientUtils.BuildClientForBilibili(config.UserAgent ?? Config.DefaultUserAgent, config.Cookie, config.HttpHandler);
			return new HttpLiveStreamRecorder(client, provider.GetRequiredService<ILogger<HttpLiveStreamRecorder>>());
		});
		services.TryAddTransient(provider =>
		{
			Config config = provider.GetRequiredService<Config>();
			HttpClient client = HttpClientUtils.BuildClientForBilibili(config.UserAgent ?? Config.DefaultUserAgent, config.Cookie, config.HttpHandler);
			return new FFmpegLiveStreamRecorder(client, provider.GetRequiredService<ILogger<FFmpegLiveStreamRecorder>>());
		});

		return services;
	}

	public static IServiceCollection AddStreamUriSelectors(this IServiceCollection services)
	{
		services.TryAddTransient(provider =>
		{
			Config config = provider.GetRequiredService<Config>();

			IStreamUriSelector res;

			switch (config.StreamHostRule)
			{
				case StreamHostRule.FirstResponse:
				{
					res = new FirstResponseStreamUriSelector();
					break;
				}
				case StreamHostRule.FastestResponse:
				{
					res = new FastestResponseStreamUriSelector();
					break;
				}
				case StreamHostRule.Random:
				{
					res = new RandomStreamUriSelector();
					break;
				}
				default:
				{
					goto case Config.DefaultStreamHostRule;
				}
			}

			return res;
		});

		return services;
	}
}
