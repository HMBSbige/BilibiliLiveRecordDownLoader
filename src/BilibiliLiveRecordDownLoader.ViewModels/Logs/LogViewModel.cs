namespace BilibiliLiveRecordDownLoader.ViewModels.Logs;

public sealed partial class LogViewModel : ViewModelBase
{
	private const int Capacity = 1000;
	private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

	private long _lastSequence;

	public LogViewModel()
	{
		OpenLogFolderCommand.DisposeWith(Disposables);
	}

	public ObservableCollection<LogEntry> Entries { get; } = [];

	internal void StartMonitoring()
	{
		ILogStore logStore = AppLocator.Current.GetRequiredService<ILogStore>();
		PollChanges(() => logStore.Version, () => Append(logStore.GetEntries()), RefreshInterval);
	}

	[ReactiveCommand]
	private async Task OpenLogFolderAsync()
	{
		await AppLocator.Current.GetRequiredService<ILauncherService>().OpenFolderAsync(ConfigurationConsts.LogDirectory);
	}

	private void Append(IReadOnlyList<LogEntry> entries)
	{
		foreach (LogEntry entry in entries.Where(entry => entry.Sequence > _lastSequence))
		{
			Entries.Add(entry);
			_lastSequence = entry.Sequence;
		}

		while (Entries.Count > Capacity)
		{
			Entries.RemoveAt(0);
		}
	}
}
