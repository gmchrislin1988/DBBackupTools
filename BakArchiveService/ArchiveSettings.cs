using System;
using System.Globalization;
using System.IO;
using System.Xml;

internal enum ArchiveCompressionMode
{
    LowResource,
    FastZip,
    Ultra
}

internal sealed class ArchiveSettings
{
    private static readonly string SettingsPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.xml");

    internal readonly string SourceDirectory;
    internal readonly string DestinationDirectory;
    internal readonly TimeSpan DailyTime;
    internal readonly ArchiveCompressionMode CompressionMode;

    internal ArchiveSettings(string source, string destination, TimeSpan dailyTime, ArchiveCompressionMode compressionMode)
    {
        SourceDirectory = NormalizeDirectory(source, "SourceDirectory");
        DestinationDirectory = NormalizeDirectory(destination, "DestinationDirectory");
        if (string.Equals(SourceDirectory, DestinationDirectory, StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Source and destination must be different directories.");
        DailyTime = dailyTime;
        CompressionMode = compressionMode;
    }

    internal static ArchiveSettings Load()
    {
        var document = new XmlDocument { XmlResolver = null };
        using (var reader = XmlReader.Create(SettingsPath))
            document.Load(reader);

        DateTime time;
        if (!DateTime.TryParseExact(ReadValue(document, "DailyTime"), "HH:mm",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            throw new FormatException("DailyTime must use HH:mm.");

        string mode = ReadValue(document, "CompressionMode");
        ArchiveCompressionMode compression = ArchiveCompressionMode.LowResource;
        if (mode.Length != 0 && (!Enum.TryParse(mode, true, out compression) ||
            !string.Equals(mode, compression.ToString(), StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("CompressionMode must be LowResource, FastZip or Ultra.");

        return new ArchiveSettings(ReadValue(document, "SourceDirectory"),
            ReadValue(document, "DestinationDirectory"), time.TimeOfDay, compression);
    }

    internal void Save()
    {
        if (!Directory.Exists(SourceDirectory) || !Directory.Exists(DestinationDirectory))
            throw new DirectoryNotFoundException("Select existing source and destination folders.");

        var document = new XmlDocument();
        document.LoadXml("<Settings><SourceDirectory/><DestinationDirectory/><DailyTime/><CompressionMode/></Settings>");
        document.SelectSingleNode("/Settings/SourceDirectory").InnerText = SourceDirectory;
        document.SelectSingleNode("/Settings/DestinationDirectory").InnerText = DestinationDirectory;
        document.SelectSingleNode("/Settings/DailyTime").InnerText =
            DailyTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        document.SelectSingleNode("/Settings/CompressionMode").InnerText = CompressionMode.ToString();

        string temporary = Path.Combine(Path.GetDirectoryName(SettingsPath), Guid.NewGuid() + ".tmp");
        try
        {
            document.Save(temporary);
            if (File.Exists(SettingsPath))
                File.Replace(temporary, SettingsPath, null);
            else
                File.Move(temporary, SettingsPath);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string ReadValue(XmlDocument document, string name)
    {
        XmlNode node = document.SelectSingleNode("/Settings/" + name);
        return node == null ? "" : node.InnerText.Trim();
    }

    private static string NormalizeDirectory(string directory, string setting)
    {
        string value = directory.Trim().Trim('"');
        string root = Path.GetPathRoot(value);
        if (string.IsNullOrEmpty(root) || root.Length == 1 || root.EndsWith(":", StringComparison.Ordinal))
            throw new FormatException(setting + " must be a full path.");
        return Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }
}
