namespace INSwitch;

static class Program
{
    [STAThread]
    static void Main()
    {
        var testInstanceId = Environment.GetEnvironmentVariable("NN_SWITCH_TEST_INSTANCE");
        var mutexName = string.IsNullOrWhiteSpace(testInstanceId)
            ? @"Local\INSwitch_20F5C5CD-2EA4-4D6B-9C65-D5D436EF8B22"
            : $@"Local\INSwitch_Test_{testInstanceId}";
        using var singleInstance = new Mutex(
            initiallyOwned: true,
            name: mutexName,
            createdNew: out var isFirstInstance);

        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => ErrorLog.Write(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ErrorLog.Write(args.ExceptionObject as Exception ?? new Exception("Unknown unhandled error."));

        using var context = new TrayApplicationContext();
        Application.Run(context);
    }
}
