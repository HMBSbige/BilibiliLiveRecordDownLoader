using BilibiliLiveRecordDownLoader.ViewModels;
using ReactiveUI;
using Splat;
using System;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace BilibiliLiveRecordDownLoader
{
	public partial class MainWindow
	{
		public MainWindow()
		{
			InitializeComponent();
			ViewModel = Locator.Current.GetService<MainWindowViewModel>();

			this.WhenActivated(d =>
			{
				ViewModel.DisposeWith(d);

				this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.NotifyIcon, nameof(NotifyIcon.TrayLeftMouseUp)).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.ShowWindowCommand, v => v.ShowMenuItem).DisposeWith(d);
				this.BindCommand(ViewModel, vm => vm.ExitCommand, v => v.ExitMenuItem).DisposeWith(d);

				#region CloseReasonHack

				AddCloseReasonHook();

				this.Events().Closing.Subscribe(e =>
				{
					if (CloseReason == CloseReason.UserClosing)
					{
						Hide();
						e.Cancel = true;
					}
				}).DisposeWith(d);

				#endregion
			});
		}

		#region CloseReasonHack

		private void AddCloseReasonHook()
		{
			if (PresentationSource.FromDependencyObject(this) is HwndSource source)
			{
				source.AddHook(WindowProc);
			}
		}

		private void RemoveCloseReasonHook()
		{
			if (PresentationSource.FromDependencyObject(this) is HwndSource source)
			{
				source.RemoveHook(WindowProc);
			}
		}

		public CloseReason CloseReason = CloseReason.None;

		private nint WindowProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
		{
			if (CloseReason is not CloseReason.UserClosing and not CloseReason.None)
			{
				RemoveCloseReasonHook();
				return 0;
			}

			switch (msg)
			{
				case 0x10:
				{
					CloseReason = CloseReason.UserClosing;
					break;
				}
				case 0x11:
				case 0x16:
				{
					CloseReason = CloseReason.WindowsShutDown;
					break;
				}
				case 0x112:
				{
					if (((ushort)wParam & 0xfff0) == 0xf060)
					{
						CloseReason = CloseReason.UserClosing;
					}

					break;
				}
			}

			return 0;
		}

		#endregion
	}
}
