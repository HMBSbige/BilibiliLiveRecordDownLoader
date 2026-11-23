using BilibiliLiveRecordDownLoader.Application.Contracts.Lifecycle;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels;
using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;
using BilibiliLiveRecordDownLoader.ViewModels.Platform;
using ReactiveUI;
using ReactiveUI.Primitives;
using System.Collections.Concurrent;
using System.Windows.Input;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class MainWindowViewModelTest
{
	[Test]
	public async Task ExitStopsAcceptingActivationsFirstAndShutsDownEvenIfStoppingFailsAsync(CancellationToken cancellationToken)
	{
		Events events = new();
		InvalidOperationException failure = new("stop failed");
		using DialogHostViewModel dialogs = new();
		using MainWindowViewModel viewModel = Create(events, isRecording: false, new FakeLifecycle(events) { Failure = failure }, dialogs);
		// 先订阅，否则异常会落到全局处理
		Task<Exception> thrown = viewModel.ExitCommand.ThrownExceptions.FirstAsync(cancellationToken);

		((ICommand)viewModel.ExitCommand).Execute(null);

		using (Assert.Multiple())
		{
			await Assert.That(await thrown).IsSameReferenceAs(failure);
			await Assert.That(events).IsEquivalentTo(["BeginShutdown", "Stop", "Shutdown"], CollectionOrdering.Matching);
		}
	}

	[Test]
	public async Task ExitWhileRecordingClosesTheOpenDialogBeforeAskingAsync(CancellationToken cancellationToken)
	{
		Events events = new();
		using DialogHostViewModel dialogs = new();
		using MainWindowViewModel viewModel = Create(events, isRecording: true, new FakeLifecycle(events), dialogs);
		Task<bool> editing = dialogs.ShowAsync(new TestDialog());
		Task<DialogViewModel?> confirmation = dialogs.WhenChanged(static dialogs => dialogs.Current).Keep(static dialog => dialog is ConfirmDialogViewModel).FirstAsync(cancellationToken);

		Task exiting = viewModel.ExitCommand.Execute().ToTask(cancellationToken);
		(await confirmation)?.Dismiss();
		await exiting;

		using (Assert.Multiple())
		{
			await Assert.That(await editing.WaitAsync(cancellationToken)).IsFalse();
			await Assert.That(events).IsEquivalentTo(["ShowMainWindow"]);
		}
	}

	[Test]
	public async Task ExitWhileADialogIsSubmittingLeavesItOpenAsync(CancellationToken cancellationToken)
	{
		Events events = new();
		using DialogHostViewModel dialogs = new();
		using MainWindowViewModel viewModel = Create(events, isRecording: true, new FakeLifecycle(events), dialogs);
		TestDialog submitting = new(isBusy: true);
		_ = dialogs.ShowAsync(submitting);

		await viewModel.ExitCommand.Execute().ToTask(cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(dialogs.Current).IsSameReferenceAs(submitting);
			await Assert.That(events).IsEquivalentTo(["ShowMainWindow"]);
		}
	}

	private static MainWindowViewModel Create(Events events, bool isRecording, FakeLifecycle lifecycle, DialogHostViewModel dialogs)
	{
		return new MainWindowViewModel(dialogs, new FakeShell(events), lifecycle, new SettingsAppService(new FakeConfigManager()), new FakeRoomAppService(isRecording), new IdleTasks());
	}

	private sealed class Events : ConcurrentQueue<string>;

	private sealed class TestDialog : DialogViewModel<bool>
	{
		public TestDialog(bool isBusy = false)
		{
			IsBusy = isBusy;
		}

		public override string Title => "test";
	}

	private sealed class FakeShell(Events events) : IApplicationShell
	{
		public bool StartMinimized => false;

		public void ShowMainWindow()
		{
			events.Enqueue(nameof(ShowMainWindow));
		}

		public void BeginShutdown()
		{
			events.Enqueue(nameof(BeginShutdown));
		}

		public void Shutdown()
		{
			events.Enqueue(nameof(Shutdown));
		}
	}

	private sealed class FakeLifecycle(Events events) : IAppLifecycleService
	{
		public Exception? Failure { get; init; }

		public Task StartAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task StopAsync()
		{
			events.Enqueue("Stop");
			return Failure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
		}
	}

	private sealed class IdleTasks : IBackgroundTaskAppService
	{
		public long Version => 0;

		public bool HasActiveTasks => false;

		public IReadOnlyList<BackgroundTaskSnapshot> GetTasks()
		{
			return [];
		}

		public void Cancel(Guid id)
		{
		}

		public void CancelAll()
		{
		}

		public void RemoveFinished()
		{
		}
	}

	private sealed class FakeRoomAppService(bool isRecording) : RoomAppServiceStub
	{
		public override bool IsRecording => isRecording;
	}
}
