namespace BilibiliLiveRecordDownLoader.Domain;

public static class DomainServiceCollectionExtensions
{
	public static IServiceCollection AddDomain(this IServiceCollection services)
	{
		services.AddDomainShared();

		return services;
	}
}
