using BilibiliLiveRecordDownLoader.Interfaces;
using BilibiliLiveRecordDownLoader.Services;
using BilibiliLiveRecordDownLoader.Utils;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using Serilog;
using Serilog.Events;
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

        public IServiceProvider ServiceProvider { get; private set; }

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
                Log.CloseAndFlush();
            });
            Current.Events().DispatcherUnhandledException.Subscribe(args =>
            {
                try
                {
                    if (Interlocked.Increment(ref _exited) != 1)
                    {
                        return;
                    }

                    var exStr = $@"未捕获异常：{args.Exception}";

                    Log.Fatal(args.Exception, @"未捕获异常");
                    MessageBox.Show(exStr, nameof(BilibiliLiveRecordDownLoader), MessageBoxButton.OK, MessageBoxImage.Error);

                    Current.Shutdown();
                }
                finally
                {
                    singleInstance.Dispose();
                    Log.CloseAndFlush();
                }
            });

            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(@"##SyncfusionLicense##");

            Locator.CurrentMutable.RegisterViewsForViewModels(Assembly.GetCallingAssembly());

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            ServiceProvider = serviceCollection.BuildServiceProvider();

            MainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            MainWindow.ShowWindow();
        }

        private void SingleInstance_ArgumentsReceived(IEnumerable<string> args)
        {
            if (args.Contains(Constants.ParameterShow))
            {
                MainWindow?.ShowWindow();
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            const string outputTemplate =
                    @"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message:lj}{NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
#if DEBUG
                    .MinimumLevel.Debug()
                    .WriteTo.Debug(outputTemplate: outputTemplate)
#else
                    .MinimumLevel.Information()
#endif
                    .MinimumLevel.Override(@"Microsoft", LogEventLevel.Information)
                    .Enrich.FromLogContext()
                    .WriteTo.Async(c => c.File(@"Logs/BilibiliLiveRecordDownLoader.log",
                            outputTemplate: outputTemplate,
                            rollOnFileSizeLimit: true,
                            retainedFileCountLimit: 2,
                            fileSizeLimitBytes: Constants.MaxLogFileSize))
                    .CreateLogger();

            services.AddSingleton<MainWindow>();
            services.AddSingleton(typeof(IConfigService), typeof(ConfigServiceService));
            services.AddLogging(c => c.AddSerilog());
        }
    }
}
