using PCSC;
using PCSC.Monitoring;
using System.Diagnostics;

if (args.Length > 1 ||
    (args.Length == 1 && !string.Equals(args[0], "--apdu", StringComparison.OrdinalIgnoreCase)))
{
    Console.Error.WriteLine("Usage: AutoICCardReaderConsole [--apdu]");
    Console.Error.WriteLine("Default mode reads and prints the card ATR; --apdu reads a Taiwan NHI card ID number.");
    Environment.ExitCode = 2;
    return;
}

var useNhiApdu = args.Length == 1;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

WriteDiagnostic(useNhiApdu
    ? "Application started in NHI APDU mode; waiting for a smart-card reader."
    : "Application started in generic ATR mode; waiting for a smart-card reader.");

while (!shutdown.IsCancellationRequested)
{
    string? readerName;
    var discoveryTimer = Stopwatch.StartNew();
    try
    {
        WriteDiagnostic("Establishing PC/SC context to enumerate readers.");
        using var context = ContextFactory.Instance.Establish(SCardScope.System);
        WriteDiagnostic($"PC/SC context established ({discoveryTimer.ElapsedMilliseconds} ms).");
        discoveryTimer.Restart();
        WriteDiagnostic("Enumerating connected readers.");
        readerName = context.GetReaders().FirstOrDefault();
        WriteDiagnostic($"Reader enumeration completed ({discoveryTimer.ElapsedMilliseconds} ms).");
    }
    catch (Exception exception)
    {
        WriteDiagnostic($"Could not enumerate smart-card readers after {discoveryTimer.ElapsedMilliseconds} ms: {exception.Message}");
        await DelayBeforeRetry(shutdown.Token);
        continue;
    }

    if (string.IsNullOrWhiteSpace(readerName))
    {
        WriteDiagnostic("No smart-card reader found.");
        await DelayBeforeRetry(shutdown.Token);
        continue;
    }

    WriteDiagnostic($"Reader found: {readerName}. Creating monitor.");

    using var monitor = MonitorFactory.Instance.Create(SCardScope.System);
    var monitorStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    monitor.CardInserted += (_, _) =>
    {
        var cardTimer = Stopwatch.StartNew();
        void ReportCardStage(string stage) =>
            WriteDiagnostic($"[card +{cardTimer.Elapsed.TotalMilliseconds:F0} ms] {stage}");

        ReportCardStage("Card insertion event received.");
        try
        {
            if (useNhiApdu)
            {
                var profile = SmartCardReader.ReadNhiProfile(readerName, ReportCardStage);
                ReportCardStage("NHI profile read complete; writing de-identified fields to stdout.");
                Console.WriteLine($"卡號：[已遮蔽]");
                Console.WriteLine($"姓名：[已遮蔽]");
                Console.WriteLine($"身分證號：[已遮蔽]");
                Console.WriteLine($"生日：[已遮蔽]");
                Console.WriteLine($"性別：[已遮蔽]");
                Console.WriteLine($"發卡日期：[已遮蔽]");
                Console.WriteLine($"APDU 回傳原始資料（HEX）：[已遮蔽]");
                ReportCardStage("De-identified NHI profile fields written to stdout.");
            }
            else
            {
                var atr = SmartCardReader.ReadAtr(readerName, ReportCardStage);
                ReportCardStage("ATR read complete; writing ATR to stdout.");
                Console.WriteLine(atr);
                ReportCardStage("ATR written to stdout.");
            }
        }
        catch (Exception exception)
        {
            ReportCardStage($"Card read failed: {exception.Message}");
        }
    };

    monitor.MonitorException += (_, _) =>
    {
        WriteDiagnostic("Reader monitoring stopped; searching for a reader again.");
        monitorStopped.TrySetResult();
    };

    try
    {
        var monitorTimer = Stopwatch.StartNew();
        WriteDiagnostic("Starting reader monitor.");
        monitor.Start(readerName);
        WriteDiagnostic($"Reader monitor started ({monitorTimer.ElapsedMilliseconds} ms); waiting for card insertion.");
        await Task.WhenAny(monitorStopped.Task, Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token));
    }
    catch (Exception exception) when (!shutdown.IsCancellationRequested)
    {
        WriteDiagnostic($"Could not monitor reader: {exception.Message}");
    }

    if (!shutdown.IsCancellationRequested)
    {
        await DelayBeforeRetry(shutdown.Token);
    }
}

static void WriteDiagnostic(string message)
{
    Console.Error.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}");
}

static async Task DelayBeforeRetry(CancellationToken cancellationToken)
{
    try
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
    }
}
