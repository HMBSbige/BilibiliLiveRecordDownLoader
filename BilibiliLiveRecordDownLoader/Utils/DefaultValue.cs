namespace BilibiliLiveRecordDownLoader.Utils;

internal static class DefaultValue
{
	private static readonly Dictionary<Type, object?> DefaultValues = new();

	public static object? Get(Type type)
	{
		if (!type.IsValueType)
		{
			return null;
		}

		if (DefaultValues.TryGetValue(type, out object? cachedValue))
		{
			return cachedValue;
		}

		object? defaultValue = Activator.CreateInstance(type);
		DefaultValues[type] = defaultValue;

		return defaultValue;
	}
}
