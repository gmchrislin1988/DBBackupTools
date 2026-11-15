using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.ServiceProcess;
using System.Threading;

internal sealed class BakCleanupService : ServiceBase
{
    private const string Name = "BakCleanupService";
    private string directory;
    private TimeSpan dailyTime;
    private readonly ManualResetEvent stopping = new ManualResetEvent(false);
    private Thread worker;

    private BakCleanupService()
    {
        ServiceName = Name;
        CanStop = true;
        AutoLog = false;
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--preview")
            {
                return Clean(Path.GetFullPath(args[1]), true, () => false,
                    (message, level) => Console.WriteLine(message)) ? 0 : 1;
            }

            if (args.Length != 0)
            {
                Console.Error.WriteLine("Usage: BakCleanupService.exe --preview <directory>");
                return 2;
            }

            ServiceBase.Run(new BakCleanupService());
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    protected override void OnStart(string[] args)
    {
        try
        {
            CleanupSettings.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.xml"),
                out directory, out dailyTime);
        }
        catch (Exception error)
        {
            WriteLog("Could not load Settings.xml: " + error.Message, EventLogEntryType.Error);
            throw;
        }

        stopping.Reset();
        worker = new Thread(Run) { IsBackground = true };
        worker.Start();
    }

    protected override void OnStop()
    {
        stopping.Set();
        worker.Join(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        DateTime started = DateTime.Now;
        DateTime lastScheduledDate = started.TimeOfDay >= dailyTime
            ? started.Date : DateTime.MinValue;
        RunCleanup();

        while (!stopping.WaitOne(TimeSpan.FromSeconds(15)))
        {
            DateTime now = DateTime.Now;
            if (now.Date > lastScheduledDate && now.TimeOfDay >= dailyTime)
            {
                lastScheduledDate = now.Date;
                RunCleanup();
            }
        }
    }

    private bool IsStopping()
    {
        return stopping.WaitOne(0);
    }

    private void RunCleanup()
    {
        string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LOGS");
        DateTime today = DateTime.Today;
        try
        {
            Directory.CreateDirectory(logDirectory);
            string filename = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";
            using (var writer = new StreamWriter(Path.Combine(logDirectory, filename), true))
            {
                writer.AutoFlush = true;
                Action<string, EventLogEntryType> log = (message, level) =>
                    writer.WriteLine("{0:O} [{1}] {2}", DateTimeOffset.Now, level, message);
                RemoveExpiredLogs(logDirectory, today, log);
                log("Starting cleanup: " + directory, EventLogEntryType.Information);
                Clean(directory, false, IsStopping, log);
            }
        }
        catch (Exception error)
        {
            WriteLog("Cleanup log or run failed: " + error.Message, EventLogEntryType.Error);
        }
    }

    private static void RemoveExpiredLogs(string logDirectory, DateTime today,
        Action<string, EventLogEntryType> log)
    {
        DateTime oldestDate = today.AddDays(-6);
        foreach (string path in Directory.EnumerateFiles(logDirectory, "*.log"))
        {
            DateTime date;
            if (!DateTime.TryParseExact(Path.GetFileName(path), "yyyy-MM-dd'.log'",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date) || date >= oldestDate)
                continue;

            try
            {
                File.Delete(path);
            }
            catch (IOException error)
            {
                log("Could not remove expired log " + path + ": " + error.Message,
                    EventLogEntryType.Warning);
            }
            catch (UnauthorizedAccessException error)
            {
                log("Could not remove expired log " + path + ": " + error.Message,
                    EventLogEntryType.Warning);
            }
        }
    }

    private static bool Clean(string directory, bool preview, Func<bool> isStopping,
        Action<string, EventLogEntryType> log)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-15);
        int matched = 0;
        int failed = 0;
        try
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The backup directory is a reparse point.");

            foreach (string path in Directory.EnumerateFiles(directory))
            {
                if (isStopping())
                    break;
                if (!string.Equals(Path.GetExtension(path), ".bak", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    FileInfo file = new FileInfo(path);
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        file.LastWriteTimeUtc >= cutoff)
                        continue;

                    if (!preview)
                        file.Delete();
                }
                catch (Exception error)
                {
                    failed++;
                    log(path + ": " + error.Message, EventLogEntryType.Error);
                    continue;
                }

                matched++;
                log((preview ? "Would delete " : "Deleted ") + path, EventLogEntryType.Information);
            }
        }
        catch (Exception error)
        {
            failed++;
            log(directory + ": " + error.Message, EventLogEntryType.Error);
        }

        log(string.Format(CultureInfo.InvariantCulture,
            "{0}: {1}. Errors: {2}. Cutoff (UTC): {3:O}. Directory: {4}",
            preview ? "Eligible files" : "Deleted files", matched, failed, cutoff, directory),
            failed == 0 ? EventLogEntryType.Information : EventLogEntryType.Warning);
        return failed == 0;
    }

    private static void WriteLog(string message, EventLogEntryType level)
    {
        try
        {
            EventLog.WriteEntry(Name, message, level);
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            Trace.TraceError(error.Message);
        }
        catch (InvalidOperationException error)
        {
            Trace.TraceError(error.Message);
        }
    }
}
