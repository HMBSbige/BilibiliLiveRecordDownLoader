namespace BilibiliLiveRecordDownLoader.ViewModels;

public static class DependencyResolverExtensions
{
	extension(IReadonlyDependencyResolver resolver)
	{
		public T GetRequiredService<T>() where T : notnull
		{
			T? service = resolver.GetService<T>();
			return service ?? throw new InvalidOperationException($"No service for type '{typeof(T)}' has been registered.");
		}
	}
}
