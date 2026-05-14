using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Threading;
using System;

namespace SnapNote;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var ctx = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(ctx);

            // Le tiret du bas ici sert maintenant correctement de "discard"
            _ = new App();
        });
    }
}