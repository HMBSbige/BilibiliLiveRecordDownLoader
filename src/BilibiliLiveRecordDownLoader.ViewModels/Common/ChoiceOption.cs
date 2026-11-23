namespace BilibiliLiveRecordDownLoader.ViewModels.Common;

public sealed record ChoiceOption(object? Value)
{
	public static ChoiceOption Inherit { get; } = new((object?)null);

	public static IReadOnlyList<ChoiceOption> Toggles { get; } = [Inherit, new(true), new(false)];

	public static IReadOnlyList<ChoiceOption> Of<T>(params IEnumerable<T> values) where T : struct, Enum
	{
		return [.. values.Select(static value => new ChoiceOption(value))];
	}

	public static IReadOnlyList<ChoiceOption> Of<T>() where T : struct, Enum
	{
		return Of(Enum.GetValues<T>());
	}

	public static IReadOnlyList<ChoiceOption> InheritOr<T>() where T : struct, Enum
	{
		return [Inherit, .. Of<T>()];
	}

	public T? As<T>() where T : struct
	{
		return Value is T value ? value : null;
	}

	public override string ToString()
	{
		return Labels.Of(Value);
	}
}
