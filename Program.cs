using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Threading;

namespace SnapNote;

class Program
{
    [STAThread]
    static void Main(string[] _)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var ctx = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(ctx);
            _ = new App();
        });
    }
}
