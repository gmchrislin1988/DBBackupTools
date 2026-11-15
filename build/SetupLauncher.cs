using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class SetupLauncher
{
    [STAThread]
    private static int Main(string[] args)
    {
        string directory = null;
        try
        {
            bool uninstall = args.Length == 1 &&
                string.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase);
            if (args.Length != 0 && !uninstall)
                throw new ArgumentException("Use /uninstall to remove the service.");

            directory = Path.Combine(Path.GetTempPath(), "DBBackupTools-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Assembly payload = Assembly.GetExecutingAssembly();
            string script = null;
            foreach (string resource in payload.GetManifestResourceNames())
            {
                string path = Path.Combine(directory, resource.Substring("Payload.".Length));
                using (Stream input = payload.GetManifestResourceStream(resource))
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    input.CopyTo(output);
                if (path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
                    script = path;
            }
            if (script == null)
                throw new InvalidDataException("The installer script is missing.");

            string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            string options = "-NoProfile -STA -ExecutionPolicy Bypass -File \"" + script + "\" " +
                (uninstall ? "-Uninstall" : "-Interactive");
            using (Process child = Process.Start(new ProcessStartInfo(powershell, options)
            {
                UseShellExecute = false,
                WorkingDirectory = directory
            }))
            {
                child.WaitForExit();
                if (child.ExitCode != 0)
                    throw new InvalidOperationException("Setup exited with code " + child.ExitCode + ".");
            }
            return 0;
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "DBBackupTools Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            if (directory != null && Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(directory))
                    File.Delete(file);
                Directory.Delete(directory, false);
            }
        }
    }
}
