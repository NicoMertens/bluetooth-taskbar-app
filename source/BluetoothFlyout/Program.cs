using Velopack;

namespace BluetoothFlyout;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Must run before any UI: install/uninstall/update hooks start the exe and expect it to exit at once.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
