using System.IO;
using System.Reflection;
using ReactiveUI;
using Splat;

namespace BilibiliLiveRecordDownLoader
{
    public partial class App
    {
        public App()
        {
            Locator.CurrentMutable.RegisterViewsForViewModels(Assembly.GetCallingAssembly());
            Directory.SetCurrentDirectory(Path.GetDirectoryName(Utils.Utils.GetExecutablePath()));
        }
    }
}
