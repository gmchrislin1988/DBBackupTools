using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

internal sealed class BakArchiveService : ServiceBase
{
    private const string Name = "BakArchiveService";
    private static readonly string LogDirectory =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LOGS");
    private readonly ManualResetEvent stopping = new ManualResetEvent(false);
    private Thread worker;
    private string source;
    private string destination;
    private TimeSpan dailyTime;
    private ArchiveCompressionMode compressionMode;

    private static string FindSevenZip()
    {
        foreach (string root in new[] { Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (string.IsNullOrEmpty(root))
                continue;
            string executable = Path.Combine(root, "7-Zip", "7z.exe");
            if (File.Exists(executable))
                return executable;
        }
        return null;
    }

    private BakArchiveService()
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
                Candidate file = SelectBackup(args[1]);
                Console.WriteLine(file == null ? "No .bak files found." :
                    string.Format(CultureInfo.InvariantCulture, "{0} ({1} bytes)", file.Path, file.Length));
                return 0;
            }
            if (args.Length != 0)
                throw new ArgumentException("Usage: BakArchiveService.exe --preview <source-directory>");

            ServiceBase.Run(new BakArchiveService());
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
            ArchiveSettings settings = ArchiveSettings.Load();
            source = settings.SourceDirectory;
            destination = settings.DestinationDirectory;
            dailyTime = settings.DailyTime;
            compressionMode = settings.CompressionMode;
        }
        catch (Exception error)
        {
            Log(error.Message, EventLogEntryType.Error);
            throw;
        }

        stopping.Reset();
        worker = new Thread(Run) { IsBackground = true };
        worker.Start();
    }

    protected override void OnStop()
    {
        stopping.Set();
        worker.Join(TimeSpan.FromSeconds(10));
    }

    private void Run()
    {
        DateTime scheduledDate = DateTime.MinValue;
        DateTime retryAt = DateTime.MinValue;
        int attempts = 0;
        do
        {
            DateTime now = DateTime.Now;
            if (now.Date > scheduledDate && now.TimeOfDay >= dailyTime)
            {
                scheduledDate = now.Date;
                attempts = 3;
                retryAt = now;
            }

            if (now.Date == scheduledDate && attempts > 0 && now >= retryAt)
            {
                attempts--;
                try
                {
                    PruneLogs();
                    Log("Starting archive run. Source: " + source + " Destination: " + destination,
                        EventLogEntryType.Information);
                    ArchiveLatest(source, destination, scheduledDate, compressionMode, () => stopping.WaitOne(0), Log);
                    attempts = 0;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception error)
                {
                    Log("Archive run failed: " + error.Message + " Attempts remaining today: " + attempts,
                        EventLogEntryType.Error);
                    retryAt = DateTime.Now.AddMinutes(5);
                }
            }
        } while (!stopping.WaitOne(TimeSpan.FromSeconds(15)));
    }

    private static Candidate SelectBackup(string directory)
    {
        TimeZoneInfo serverTime = TimeZoneInfo.Local;
        return Directory.EnumerateFiles(directory)
            .Where(path => string.Equals(Path.GetExtension(path), ".bak", StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .Where(file => (file.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0)
            .Select(file => new Candidate
            {
                Path = file.FullName,
                Length = file.Length,
                ModifiedUtc = file.LastWriteTimeUtc
            })
            .OrderByDescending(file => TimeZoneInfo.ConvertTimeFromUtc(file.ModifiedUtc, serverTime).Date)
            .ThenByDescending(file => file.Length)
            .ThenByDescending(file => file.ModifiedUtc)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static void ArchiveLatest(string sourceDirectory, string destinationDirectory, DateTime archiveDate,
        ArchiveCompressionMode compressionMode, Func<bool> isStopping, Action<string, EventLogEntryType> log)
    {
        Candidate file = SelectBackup(sourceDirectory);
        if (file == null)
        {
            log("No .bak files found in " + sourceDirectory, EventLogEntryType.Information);
            return;
        }

        string name = Path.GetFileName(file.Path);
        bool fastZip = compressionMode == ArchiveCompressionMode.FastZip;
        bool ultra = compressionMode == ArchiveCompressionMode.Ultra;
        string executable = fastZip ? null : FindSevenZip();
        string extension = executable == null ? ".zip" : ".7z";
        string method = executable == null ? (fastZip ? "Fast ZIP Deflate" : "ZIP Deflate") :
            (ultra ? "LZMA2 Ultra" : "LZMA2 Low Resource");
        string archiveName = ArchivePrefix(archiveDate) + Path.ChangeExtension(name, extension);
        string archivePath = Path.Combine(destinationDirectory, archiveName);
        log("Selected backup: " + file.Path + " Compression: " + method, EventLogEntryType.Information);

        string tempPath = Path.Combine(destinationDirectory, ".BakArchive-" + Guid.NewGuid() + ".tmp");
        try
        {
            using (var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length != file.Length || File.GetLastWriteTimeUtc(file.Path) != file.ModifiedUtc)
                    throw new IOException("Backup changed before it could be archived: " + file.Path);

                if (executable == null)
                {
                    CreateZip(input, tempPath, name, fastZip ? CompressionLevel.Fastest : CompressionLevel.Optimal,
                        isStopping);
                }
                else
                {
                    string compression = ultra ? "-mx=9 -mmt=2" : "-mx=3 -md=16m -mmt=1";
                    RunSevenZip(executable, "a -t7z -m0=LZMA2 " + compression + " -spd -- " +
                        QuotePath(tempPath) + " " + QuotePath(file.Path), isStopping);
                    RunSevenZip(executable, "t -t7z -- " + QuotePath(tempPath), isStopping);
                }
                if (isStopping())
                    throw new OperationCanceledException();
                PublishArchive(tempPath, archivePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        long archiveSize = new FileInfo(archivePath).Length;
        string sizeSummary = string.Format(CultureInfo.InvariantCulture, "{0} bytes -> {1} bytes", file.Length, archiveSize);
        if (file.Length > 0)
            sizeSummary += string.Format(CultureInfo.InvariantCulture, " ({0:P1} of original)",
                (double)archiveSize / file.Length);
        log("Archived " + file.Path + " to " + archivePath + " using " + method + ". " + sizeSummary,
            EventLogEntryType.Information);

        string previousPrefix = ArchivePrefix(archiveDate.AddDays(-1));
        foreach (string path in Directory.EnumerateFiles(destinationDirectory, previousPrefix + "*"))
        {
            if (isStopping())
                throw new OperationCanceledException();
            string previousExtension = Path.GetExtension(path);
            if (!Path.GetFileName(path).StartsWith(previousPrefix, StringComparison.OrdinalIgnoreCase) ||
                !(string.Equals(previousExtension, ".7z", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(previousExtension, ".zip", StringComparison.OrdinalIgnoreCase)) ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                continue;

            File.Delete(path);
            log("Deleted previous day's archive: " + path, EventLogEntryType.Information);
        }
    }

    private static void CreateZip(Stream input, string path, string name, CompressionLevel level, Func<bool> isStopping)
    {
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
        using (var entry = archive.CreateEntry(name, level).Open())
        {
            var buffer = new byte[1024 * 1024];
            int count;
            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (isStopping())
                    throw new OperationCanceledException();
                entry.Write(buffer, 0, count);
            }
        }
    }

    private static string QuotePath(string path)
    {
        return "\"" + path + "\"";
    }

    private static void RunSevenZip(string executable, string arguments, Func<bool> isStopping)
    {
        if (isStopping())
            throw new OperationCanceledException();

        var options = new ProcessStartInfo(executable, "-bso0 -bse2 -bsp0 " + arguments)
        {
            WorkingDirectory = Path.GetDirectoryName(executable),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        using (Process process = Process.Start(options))
        {
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (System.ComponentModel.Win32Exception error)
            {
                Trace.TraceWarning(error.Message);
            }
            catch (InvalidOperationException error)
            {
                Trace.TraceWarning(error.Message);
            }
            var errors = process.StandardError.ReadToEndAsync();
            while (!process.WaitForExit(250))
            {
                if (isStopping())
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        if (!process.HasExited)
                            throw;
                    }
                    process.WaitForExit();
                    throw new OperationCanceledException();
                }
            }

            if (process.ExitCode != 0)
                throw new IOException("7-Zip exited with code " + process.ExitCode + ": " + errors.Result.Trim());
        }
    }

    private static string ArchivePrefix(DateTime date)
    {
        return "BakArchive-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-";
    }

    private static void PublishArchive(string tempPath, string archivePath)
    {
        if (!File.Exists(archivePath))
        {
            File.Move(tempPath, archivePath);
            return;
        }

        string previousPath = tempPath + ".previous";
        File.Move(archivePath, previousPath);
        try
        {
            File.Move(tempPath, archivePath);
        }
        catch (IOException)
        {
            File.Move(previousPath, archivePath);
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            File.Move(previousPath, archivePath);
            throw;
        }
        File.Delete(previousPath);
    }

    private static void Log(string message, EventLogEntryType level)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        try
        {
            Directory.CreateDirectory(LogDirectory);
            string filename = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log";
            File.AppendAllText(Path.Combine(LogDirectory, filename),
                string.Format(CultureInfo.InvariantCulture, "{0:O} [{1}] {2}{3}",
                    now, level, message, Environment.NewLine));
        }
        catch (IOException error)
        {
            WriteEvent("Could not write operation log: " + error.Message, EventLogEntryType.Error);
        }
        catch (UnauthorizedAccessException error)
        {
            WriteEvent("Could not write operation log: " + error.Message, EventLogEntryType.Error);
        }

    }

    private static void PruneLogs()
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            DateTime oldestDate = DateTime.Today.AddDays(-6);
            foreach (string path in Directory.EnumerateFiles(LogDirectory, "*.log"))
            {
                DateTime date;
                if (DateTime.TryParseExact(Path.GetFileName(path), "yyyy-MM-dd'.log'",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date) &&
                    date < oldestDate && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                    File.Delete(path);
            }
        }
        catch (IOException error)
        {
            Log("Could not remove expired operation logs: " + error.Message, EventLogEntryType.Warning);
        }
        catch (UnauthorizedAccessException error)
        {
            Log("Could not remove expired operation logs: " + error.Message, EventLogEntryType.Warning);
        }
    }

    private static void WriteEvent(string message, EventLogEntryType level)
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
        catch (System.Security.SecurityException error)
        {
            Trace.TraceError(error.Message);
        }
    }

    private sealed class Candidate
    {
        internal string Path;
        internal long Length;
        internal DateTime ModifiedUtc;
    }
}
