using BilibiliLiveRecordDownLoader.Utils;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;

namespace BilibiliLiveRecordDownLoader
{
    public partial class App
    {
        private static int _exited;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Utils.Utils.GetExecutablePath()));
            const string identifier = @"Global\BilibiliLiveRecordDownLoader";

            var singleInstance = new SingleInstance.SingleInstance(identifier);
            if (!singleInstance.IsFirstInstance)
            {
                singleInstance.PassArgumentsToFirstInstance(e.Args.Append(Constants.ParameterShow));
                Current.Shutdown();
                return;
            }

            singleInstance.ArgumentsReceived.ObserveOnDispatcher().Subscribe(SingleInstance_ArgumentsReceived);
            singleInstance.ListenForArgumentsFromSuccessiveInstances();

            Current.Events().Exit.Subscribe(args =>
            {
                singleInstance.Dispose();
            });
            Current.Events().DispatcherUnhandledException.Subscribe(args =>
            {
                if (Interlocked.Increment(ref _exited) != 1)
                {
                    return;
                }

                MessageBox.Show($@"未捕获异常：{args.Exception}", nameof(BilibiliLiveRecordDownLoader), MessageBoxButton.OK, MessageBoxImage.Error);

                singleInstance.Dispose();

                Current.Shutdown();
            });

            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(@"##SyncfusionLicense##");

            Locator.CurrentMutable.RegisterViewsForViewModels(Assembly.GetCallingAssembly());

            MainWindow = new MainWindow();
            MainWindow.ShowWindow();
        }

        private void SingleInstance_ArgumentsReceived(IEnumerable<string> args)
        {
            if (args.Contains(Constants.ParameterShow))
            {
                MainWindow?.ShowWindow();
            }
        }
    }
}
