using System;
using System.Drawing;
using System.IO;
using System.ServiceProcess;
using System.Windows.Forms;

internal sealed class BakCleanupSettings : Form
{
    private const string ServiceName = "BakCleanupService";
    private readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.xml");
    private readonly TextBox folder = new TextBox();
    private readonly DateTimePicker time = new DateTimePicker();
    private readonly Label notice = new Label();
    private readonly Button save = new Button();

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var form = new BakCleanupSettings())
            Application.Run(form);
    }

    private BakCleanupSettings()
    {
        Text = "BAK Backup Cleanup Settings";
        ClientSize = new Size(540, 228);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        var folderLabel = new Label { Text = "Backup folder", AutoSize = true, Location = new Point(16, 16) };
        folder.SetBounds(16, 40, 403, 24);
        var browse = new Button { Text = "Browse..." };
        browse.SetBounds(427, 38, 96, 28);
        browse.Click += Browse;

        var timeLabel = new Label
        {
            Text = "Daily cleanup time (server local time)",
            AutoSize = true,
            Location = new Point(16, 78)
        };
        time.Format = DateTimePickerFormat.Custom;
        time.CustomFormat = "HH:mm";
        time.ShowUpDown = true;
        time.Value = DateTime.Today.AddHours(23);
        time.SetBounds(16, 102, 95, 24);

        notice.Text = "Saving restarts the service and immediately runs cleanup using the new settings.";
        notice.SetBounds(16, 142, 507, 36);
        save.Text = "Save";
        save.SetBounds(325, 181, 96, 30);
        save.Click += Save;
        var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel };
        close.SetBounds(427, 181, 96, 30);
        close.Click += (sender, args) => Close();

        Controls.AddRange(new Control[] { folderLabel, folder, browse, timeLabel, time, notice, save, close });
        AcceptButton = save;
        CancelButton = close;

        try
        {
            string directory;
            TimeSpan schedule;
            CleanupSettings.Load(settingsPath, out directory, out schedule);
            folder.Text = directory;
            time.Value = DateTime.Today.Add(schedule);
        }
        catch (Exception error)
        {
            notice.Text = "Could not load settings. Enter both fields before saving.";
            MessageBox.Show(this, error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void Browse(object sender, EventArgs args)
    {
        using (var dialog = new FolderBrowserDialog { ShowNewFolderButton = false })
        {
            dialog.Description = "Select the folder containing your backup files.";
            if (Directory.Exists(folder.Text))
                dialog.SelectedPath = folder.Text;
            if (dialog.ShowDialog(this) == DialogResult.OK)
                folder.Text = dialog.SelectedPath;
        }
    }

    private async void Save(object sender, EventArgs args)
    {
        string directory = folder.Text.Trim().Trim('"');
        DateTime schedule = time.Value;
        Enabled = false;
        UseWaitCursor = true;
        try
        {
            await System.Threading.Tasks.Task.Run(() => SaveAndApply(directory, schedule));
        }
        catch (Exception error)
        {
            Enabled = true;
            MessageBox.Show(this, error.Message, "Could not apply settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            UseWaitCursor = false;
            Enabled = true;
        }
        MessageBox.Show(this, "Settings saved and the service has restarted.",
            Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SaveAndApply(string directory, DateTime schedule)
    {
        using (var service = new ServiceController(ServiceName))
        {
            ServiceControllerStatus status = service.Status;
            CleanupSettings.Save(settingsPath, directory, schedule);
            var timeout = TimeSpan.FromSeconds(30);
            if (status != ServiceControllerStatus.Stopped)
            {
                if (status != ServiceControllerStatus.StopPending)
                    service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            }
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, timeout);
        }
    }
}
