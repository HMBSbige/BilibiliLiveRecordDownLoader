global using BilibiliApi.Clients;
global using BilibiliApi.Model.RoomInfo;
global using BilibiliLiveDanmuPreviewer;
global using JetBrains.Annotations;
global using Microsoft;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
global using Microsoft.Extensions.Logging;
global using Serilog;
global using System.Net;
global using System.Reactive.Linq;
global using Volo.Abp;
global using Volo.Abp.Autofac;
global using Volo.Abp.DependencyInjection;
global using Volo.Abp.Modularity;
global using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace BilibiliLiveDanmuPreviewer;

[DependsOn(
	typeof(AbpAutofacModule)
)]
[UsedImplicitly]
internal class BilibiliLiveDanmuPreviewerModule : AbpModule
{
	public override Task PreConfigureServicesAsync(ServiceConfigurationContext context)
	{
		context.Services.ReplaceConfiguration(new ConfigurationBuilder().AddJsonFile(@"appsettings.json", true, true).Build());

		return Task.CompletedTask;
	}

	public override Task ConfigureServicesAsync(ServiceConfigurationContext context)
	{
		IConfiguration configuration = context.Services.GetConfiguration();

		context.Services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(new LoggerConfiguration().ReadFrom.Configuration(configuration).CreateLogger(), true));
#if DEBUG
		Serilog.Debugging.SelfLog.Enable(msg =>
		{
			System.Diagnostics.Debug.Print(msg);
			System.Diagnostics.Debugger.Break();
		});
#endif

		context.Services.AddHttpClient(@"bilibili").ConfigureHttpClient((provider, client) =>
		{
			client.DefaultRequestVersion = HttpVersion.Version20;
			client.Timeout = TimeSpan.FromSeconds(10);
			client.DefaultRequestHeaders.Accept.ParseAdd(@"application/json, text/javascript, */*; q=0.01");
			client.DefaultRequestHeaders.Referrer = new Uri(@"https://live.bilibili.com/");

			IConfiguration config = provider.GetRequiredService<IConfiguration>();

			IConfigurationSection httpClientConfig = config.GetSection(@"HttpClient");
			string? cookie = httpClientConfig.GetValue<string>(@"Cookie");
			string? userAgent = httpClientConfig.GetValue<string>(@"UserAgent");

			if (!string.IsNullOrWhiteSpace(cookie))
			{
				client.DefaultRequestHeaders.Add(@"Cookie", cookie);
			}

			if (!string.IsNullOrWhiteSpace(userAgent))
			{
				client.DefaultRequestHeaders.UserAgent.Clear();
				client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
			}
		});

		context.Services.TryAddSingleton(provider =>
		{
			IHttpClientFactory httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
			return new BilibiliApiClient(httpClientFactory.CreateClient(@"bilibili"));
		});

		context.Services.AddDistributedMemoryCache();

		context.Services.TryAddTransient<IDanmuClient, WssDanmuClient>();

		return Task.CompletedTask;
	}

	public override Task OnApplicationShutdownAsync(ApplicationShutdownContext context)
	{
		context.ServiceProvider.GetRequiredService<ILoggerProvider>().Dispose();

		return Task.CompletedTask;
	}
}
