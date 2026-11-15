using System;
using System.Drawing;
using System.IO;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class BakArchiveSettings : Form
{
    private readonly TextBox source = new TextBox();
    private readonly TextBox destination = new TextBox();
    private readonly DateTimePicker time = new DateTimePicker();
    private readonly ComboBox compression = new ComboBox();
    private readonly Label notice = new Label();

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var form = new BakArchiveSettings())
            Application.Run(form);
    }

    private BakArchiveSettings()
    {
        Text = "BAK Archive Service Settings";
        ClientSize = new Size(550, 340);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        AddFolderInput("Source folder (.bak files)", source, 16);
        AddFolderInput("Destination folder (7z or ZIP archives)", destination, 76);
        var label = new Label
        {
            Text = "Daily run time (server local time)",
            AutoSize = true,
            Location = new Point(16, 137)
        };
        time.Format = DateTimePickerFormat.Custom;
        time.CustomFormat = "HH:mm";
        time.ShowUpDown = true;
        time.Value = DateTime.Today.AddHours(23);
        time.SetBounds(16, 160, 100, 24);
        var compressionLabel = new Label { Text = "Compression mode", AutoSize = true, Location = new Point(16, 193) };
        compression.DropDownStyle = ComboBoxStyle.DropDownList;
        compression.Items.AddRange(new object[] { "Low resource (7z)", "Fast ZIP", "Ultra (7z)" });
        compression.SelectedIndex = 0;
        compression.SetBounds(16, 216, 215, 24);

        notice.Text = "Saving restarts the service. If today's scheduled time has passed, it will run once.";
        notice.SetBounds(16, 249, 518, 36);
        var save = new Button { Text = "Save" };
        save.SetBounds(334, 292, 96, 30);
        save.Click += Save;
        var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel };
        close.SetBounds(438, 292, 96, 30);
        close.Click += (sender, args) => Close();
        Controls.AddRange(new Control[] { label, time, compressionLabel, compression, notice, save, close });
        AcceptButton = save;
        CancelButton = close;

        try
        {
            ArchiveSettings settings = ArchiveSettings.Load();
            source.Text = settings.SourceDirectory;
            destination.Text = settings.DestinationDirectory;
            time.Value = DateTime.Today.Add(settings.DailyTime);
            compression.SelectedIndex = (int)settings.CompressionMode;
        }
        catch (Exception error)
        {
            notice.Text = "Could not read settings. " + error.Message;
        }
    }

    private void AddFolderInput(string caption, TextBox input, int top)
    {
        var label = new Label { Text = caption, AutoSize = true, Location = new Point(16, top) };
        input.SetBounds(16, top + 23, 414, 24);
        var browse = new Button { Text = "Browse..." };
        browse.SetBounds(438, top + 21, 96, 28);
        browse.Click += (sender, args) =>
        {
            using (var dialog = new FolderBrowserDialog { ShowNewFolderButton = false, Description = caption })
            {
                if (Directory.Exists(input.Text))
                    dialog.SelectedPath = input.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    input.Text = dialog.SelectedPath;
            }
        };
        Controls.AddRange(new Control[] { label, input, browse });
    }

    private async void Save(object sender, EventArgs args)
    {
        Enabled = false;
        UseWaitCursor = true;
        try
        {
            var settings = new ArchiveSettings(source.Text, destination.Text, time.Value.TimeOfDay,
                (ArchiveCompressionMode)compression.SelectedIndex);
            await Task.Run(() => SaveAndApply(settings));
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

        MessageBox.Show(this, "Settings saved and the service has restarted.", Text,
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void SaveAndApply(ArchiveSettings settings)
    {
        using (var service = new ServiceController("BakArchiveService"))
        {
            ServiceControllerStatus status = service.Status;
            settings.Save();
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
