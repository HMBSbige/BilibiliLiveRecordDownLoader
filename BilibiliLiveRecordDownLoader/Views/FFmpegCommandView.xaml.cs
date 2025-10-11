using BilibiliLiveRecordDownLoader.Utils;
using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows;

namespace BilibiliLiveRecordDownLoader.Views;

public partial class FFmpegCommandView
{
	public FFmpegCommandView(FFmpegCommandViewModel viewModel)
	{
		InitializeComponent();
		ViewModel = viewModel;

		this.WhenActivated(d =>
		{
			this.OneWayBind(ViewModel, vm => vm.FFmpegStatus, vm => vm.FFmpegStatusTextBlock.Text).DisposeWith(d);
			this.OneWayBind(ViewModel, vm => vm.FFmpegStatusForeground, vm => vm.FFmpegStatusTextBlock.Foreground).DisposeWith(d);
			this.BindCommand(ViewModel, vm => vm.CheckFFmpegStatusCommand, vm => vm.FFmpegStatusTextBlock, nameof(FFmpegStatusTextBlock.MouseLeftButtonUp)).DisposeWith(d);

			this.Bind(ViewModel, vm => vm.CutInput, vm => vm.CutInputTextBox.Text).DisposeWith(d);
			this.Bind(ViewModel, vm => vm.CutOutput, vm => vm.CutOutputTextBox.Text).DisposeWith(d);
			this.Bind(ViewModel, vm => vm.CutStartTime, vm => vm.CutStartTimeTextBox.Text).DisposeWith(d);
			this.Bind(ViewModel, vm => vm.CutEndTime, vm => vm.CutEndTimeTextBox.Text).DisposeWith(d);

			this.Bind(ViewModel, vm => vm.ConvertInput, vm => vm.ConvertInputTextBox.Text).DisposeWith(d);
			this.Bind(ViewModel, vm => vm.ConvertOutput, vm => vm.ConvertOutputTextBox.Text).DisposeWith(d);
			this.Bind(ViewModel, vm => vm.IsDelete, vm => vm.IsDeleteToggleSwitch.IsOn).DisposeWith(d);
			this.Bind(ViewModel, vm => vm.IsFlvFixConvert, vm => vm.IsFlvFixConvertToggleSwitch.IsOn).DisposeWith(d);

			this.BindCommand(ViewModel, vm => vm.CutOpenFileCommand, vm => vm.CutInputButton).DisposeWith(d);
			this.BindCommand(ViewModel, vm => vm.CutSaveFileCommand, vm => vm.CutOutputButton).DisposeWith(d);
			this.BindCommand(ViewModel, vm => vm.CutCommand, vm => vm.CutButton).DisposeWith(d);

			this.BindCommand(ViewModel, vm => vm.ConvertOpenFileCommand, vm => vm.ConvertInputButton).DisposeWith(d);
			this.BindCommand(ViewModel, vm => vm.ConvertSaveFileCommand, vm => vm.ConvertOutputButton).DisposeWith(d);
			this.BindCommand(ViewModel, vm => vm.ConvertCommand, vm => vm.ConvertButton).DisposeWith(d);

			ViewModel.CheckFFmpegStatusCommand
				.Execute()
				.ObserveOn(RxApp.MainThreadScheduler)
				.Subscribe(b =>
				{
					HyperlinkButton.Visibility = b ? Visibility.Collapsed : Visibility.Visible;
					CutButton.IsEnabled = b;
					ConvertButton.IsEnabled = b;
				}).DisposeWith(d);

			CutInputTextBox.ShowDragOverIconEvent().DisposeWith(d);
			CutOutputTextBox.ShowDragOverIconEvent().DisposeWith(d);
			ConvertInputTextBox.ShowDragOverIconEvent().DisposeWith(d);
			ConvertOutputTextBox.ShowDragOverIconEvent().DisposeWith(d);

			CutInputTextBox.DropPathEvent().DisposeWith(d);
			CutOutputTextBox.DropPathEvent().DisposeWith(d);
			ConvertInputTextBox.DropPathEvent().DisposeWith(d);
			ConvertOutputTextBox.DropPathEvent().DisposeWith(d);
		});
	}
}
