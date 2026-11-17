namespace BilibiliLiveRecordDownLoader.ViewModels.Common;

public sealed record ChoiceOption(object? Value)
{
	public static ChoiceOption Inherit { get; } = new((object?)null);

	public static IReadOnlyList<ChoiceOption> Toggles { get; } = [Inherit, new(true), new(false)];

	public static IReadOnlyList<ChoiceOption> Of<T>(params ReadOnlySpan<T> values) where T : struct, Enum
	{
		List<ChoiceOption> options = new(values.Length);

		foreach (T value in values)
		{
			options.Add(new ChoiceOption(value));
		}

		return options;
	}

	public static IReadOnlyList<ChoiceOption> OfAll<T>() where T : struct, Enum
	{
		return Of<T>(Enum.GetValues<T>());
	}

	public static IReadOnlyList<ChoiceOption> InheritOr<T>() where T : struct, Enum
	{
		return [Inherit, .. OfAll<T>()];
	}

	public T? As<T>() where T : struct
	{
		return Value is T value ? value : null;
	}
}
