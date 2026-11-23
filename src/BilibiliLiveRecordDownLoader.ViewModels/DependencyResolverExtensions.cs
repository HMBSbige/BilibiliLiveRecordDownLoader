namespace BilibiliLiveRecordDownLoader.ViewModels;

public static class DependencyResolverExtensions
{
	extension(IReadonlyDependencyResolver resolver)
	{
		public T GetRequiredService<T>() where T : notnull
		{
			return resolver.GetService<T>() ?? throw new InvalidOperationException($"No service for type '{typeof(T)}' has been registered.");
		}
	}
}
