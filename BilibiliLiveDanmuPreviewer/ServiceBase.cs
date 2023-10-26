namespace BilibiliLiveDanmuPreviewer;

public abstract class ServiceBase : ITransientDependency
{
	public IAbpLazyServiceProvider LazyServiceProvider { get; set; } = null!; // 属性注入

	protected IServiceProvider ServiceProvider => LazyServiceProvider.LazyGetRequiredService<IServiceProvider>();

	protected ILoggerFactory LoggerFactory => LazyServiceProvider.LazyGetRequiredService<ILoggerFactory>();

	protected ILogger Logger => LazyServiceProvider.LazyGetService<ILogger>(_ => LoggerFactory.CreateLogger(GetType()));

	protected IConfiguration Configuration => LazyServiceProvider.LazyGetRequiredService<IConfiguration>();
}
