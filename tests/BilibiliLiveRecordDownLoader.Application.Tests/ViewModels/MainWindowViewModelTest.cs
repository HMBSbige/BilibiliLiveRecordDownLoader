using BilibiliLiveRecordDownLoader.Application.Contracts.Lifecycle;
using BilibiliLiveRecordDownLoader.Application.Tests.Fakes;
using BilibiliLiveRecordDownLoader.ViewModels;
using BilibiliLiveRecordDownLoader.ViewModels.Dialogs;
using BilibiliLiveRecordDownLoader.ViewModels.Platform;
using ReactiveUI.Primitives;
using System.Collections.Concurrent;
using System.Windows.Input;
using TUnit.Assertions.Enums;

namespace BilibiliLiveRecordDownLoader.Application.Tests.ViewModels;

public sealed class MainWindowViewModelTest
{
	[Test]
	[Timeout(30_000)]
	public async Task ExitStopsAcceptingActivationsFirstAndShutsDownEvenIfStoppingFailsAsync(CancellationToken cancellationToken)
	{
		Events events = new();
		InvalidOperationException failure = new("stop failed");
		using MainWindowViewModel viewModel = Create(events, isRecording: false, new FakeLifecycle(events) { Failure = failure }, out _);
		ConcurrentQueue<Exception> thrown = new();
		using IDisposable subscription = viewModel.ExitCommand.ThrownExceptions.Subscribe(thrown.Enqueue);

		((ICommand)viewModel.ExitCommand).Execute(null);
		// 异常是异步交给订阅者的，等它到了再结束，否则会落到全局处理
		await TestWait.UntilAsync(() => !thrown.IsEmpty, "the exception reaches the subscriber", cancellationToken);
		Exception exception = await Assert.That(thrown).HasSingleItem();

		using (Assert.Multiple())
		{
			await Assert.That(events).IsEquivalentTo(["BeginShutdown", "Stop", "Shutdown"], CollectionOrdering.Matching);
			await Assert.That(exception).IsSameReferenceAs(failure);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task ExitWhileRecordingClosesTheOpenDialogBeforeAskingAsync(CancellationToken cancellationToken)
	{
		Events events = new();
		using MainWindowViewModel viewModel = Create(events, isRecording: true, new FakeLifecycle(events), out DialogHostViewModel dialogs);
		Task<bool> editing = dialogs.ShowAsync(new TestDialog());

		((ICommand)viewModel.ExitCommand).Execute(null);
		await TestWait.UntilAsync(() => dialogs.Current is ConfirmDialogViewModel, "the exit confirmation is shown", cancellationToken);
		bool editingResult = await editing.WaitAsync(cancellationToken);
		dialogs.Current?.Dismiss();
		await TestWait.UntilAsync(() => ((ICommand)viewModel.ExitCommand).CanExecute(null), "the exit command finishes", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(editingResult).IsFalse();
			await Assert.That(events).IsEquivalentTo(["ShowMainWindow"]);
		}
	}

	[Test]
	[Timeout(30_000)]
	public async Task ExitWhileADialogIsSubmittingLeavesItOpenAsync(CancellationToken cancellationToken)
	{
		Events events = new();
		using MainWindowViewModel viewModel = Create(events, isRecording: true, new FakeLifecycle(events), out DialogHostViewModel dialogs);
		TestDialog submitting = new();
		submitting.MarkBusy();
		_ = dialogs.ShowAsync(submitting);

		((ICommand)viewModel.ExitCommand).Execute(null);
		await TestWait.UntilAsync(() => ((ICommand)viewModel.ExitCommand).CanExecute(null), "the exit command finishes", cancellationToken);

		using (Assert.Multiple())
		{
			await Assert.That(dialogs.Current).IsSameReferenceAs(submitting);
			await Assert.That(events).IsEquivalentTo(["ShowMainWindow"]);
		}

		submitting.Dismiss();
	}

	private static MainWindowViewModel Create(Events events, bool isRecording, FakeLifecycle lifecycle, out DialogHostViewModel dialogs)
	{
		dialogs = new DialogHostViewModel();
		return new MainWindowViewModel(dialogs, new FakeShell(events), lifecycle, new FakeConfigManager(), new FakeRoomAppService(isRecording), new IdleTasks());
	}

	private sealed class Events : ConcurrentQueue<string>;

	private sealed class TestDialog : DialogViewModel<bool>
	{
		public override string Title => "test";

		public void MarkBusy()
		{
			IsBusy = true;
		}
	}

	private sealed class FakeShell(Events events) : IApplicationShell
	{
		public CancellationToken ShuttingDown => CancellationToken.None;

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
