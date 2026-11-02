namespace BilibiliLiveRecordDownLoader.Application.Contracts;

public static class ApplicationContractsServiceCollectionExtensions
{
	public static IServiceCollection AddApplicationContracts(this IServiceCollection services)
	{
		services.AddDomainShared();

		return services;
	}
}
