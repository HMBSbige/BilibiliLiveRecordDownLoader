namespace BilibiliLiveRecordDownLoader.Domain.Shared;

public static class DomainSharedServiceCollectionExtensions
{
	public static IServiceCollection AddDomainShared(this IServiceCollection services)
	{
		services.TryAddSingleton(TimeProvider.System);

		return services;
	}
}
