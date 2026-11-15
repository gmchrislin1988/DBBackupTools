using System;
using System.Globalization;
using System.IO;
using System.Xml;

internal static class CleanupSettings
{
    internal static void Load(string path, out string backupDirectory, out TimeSpan cleanupTime)
    {
        var document = new XmlDocument { XmlResolver = null };
        using (XmlReader reader = XmlReader.Create(path))
            document.Load(reader);

        XmlNode folder = document.SelectSingleNode("/Settings/BackupDirectory");
        XmlNode schedule = document.SelectSingleNode("/Settings/DailyTime");
        if (folder == null || schedule == null)
            throw new FormatException("Settings.xml must contain BackupDirectory and DailyTime.");

        DateTime time;
        if (!DateTime.TryParseExact(schedule.InnerText.Trim(), "HH:mm",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            throw new FormatException("DailyTime must use the 24-hour HH:mm format.");

        backupDirectory = FullPath(folder.InnerText.Trim());
        cleanupTime = time.TimeOfDay;
    }

    internal static void Save(string path, string directory, DateTime time)
    {
        string fullPath = FullPath(directory);
        var folder = new DirectoryInfo(fullPath);
        if (!folder.Exists || (folder.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Select an existing backup directory that is not a reparse point.");

        var document = new XmlDocument();
        document.LoadXml("<Settings><BackupDirectory/><DailyTime/></Settings>");
        document.SelectSingleNode("/Settings/BackupDirectory").InnerText = fullPath;
        document.SelectSingleNode("/Settings/DailyTime").InnerText =
            time.ToString("HH:mm", CultureInfo.InvariantCulture);

        string temporaryPath = Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid() + ".tmp");
        try
        {
            document.Save(temporaryPath);
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string FullPath(string path)
    {
        string root = Path.GetPathRoot(path);
        if (string.IsNullOrEmpty(root) || root.Length == 1 || root.EndsWith(":", StringComparison.Ordinal))
            throw new FormatException("BackupDirectory must be a full path.");
        return Path.GetFullPath(path);
    }
}
