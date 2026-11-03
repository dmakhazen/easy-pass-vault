using System.Windows;
namespace EasyPassVault.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Lets CI verify the self-contained host without opening a desktop window,
        // reading user data, or registering anything in Windows.
        if (e.Args.SequenceEqual(new[] { "--verify-startup" }))
        {
            _ = typeof(EasyPassVault.Core.VaultSession).Assembly.GetName();
            Shutdown(0); return;
        }
        base.OnStartup(e);
    }
}
