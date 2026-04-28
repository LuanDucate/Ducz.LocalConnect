namespace Ducz.LocalConnect.App;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new DuczLocalConnectForm());
    }    
}