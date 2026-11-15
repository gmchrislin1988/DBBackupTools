#requires -Version 4.0
[CmdletBinding(DefaultParameterSetName = 'CommandLine')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'CommandLine')]
    [string]$BackupDirectory,

    [Parameter(ParameterSetName = 'CommandLine')]
    [ValidatePattern('^(?:[01][0-9]|2[0-3]):[0-5][0-9]$')]
    [string]$DailyTime = '23:00',

    [Parameter(ParameterSetName = 'CommandLine')]
    [switch]$Preview,

    [Parameter(Mandatory = $true, ParameterSetName = 'Interactive')]
    [switch]$Interactive,

    [Parameter(Mandatory = $true, ParameterSetName = 'Uninstall')]
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
trap {
    if ($Interactive -or $Uninstall) {
        $detail = $_ | Out-String
        $logPath = Join-Path ([IO.Path]::GetTempPath()) 'BakCleanupService-SetupError.log'
        Write-Host $detail -ForegroundColor Red
        try {
            Set-Content -LiteralPath $logPath -Value $detail -Encoding UTF8
            Write-Host "Setup error log: $logPath"
        }
        catch {
            Write-Host "Could not write the setup error log: $($_.Exception.Message)"
        }
        $null = Read-Host 'Press Enter to close this error window'
        exit 1
    }
    break
}

$serviceName = 'BakCleanupService'
$programFilesDirectory = [Environment]::GetFolderPath('ProgramFiles')
$installDirectory = Join-Path $programFilesDirectory $serviceName
$settingsPath = Join-Path $installDirectory 'Settings.xml'
$installedExe = Join-Path $installDirectory "$serviceName.exe"
$installedSettingsExe = Join-Path $installDirectory 'BakCleanupSettings.exe'
$binaryPath = '"{0}"' -f $installedExe

function Write-CleanupSettings {
    param([IO.DirectoryInfo]$Directory, [string]$Time)

    if (($Directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'The backup directory is a reparse point.'
    }
    $settings = [xml]'<Settings><BackupDirectory/><DailyTime/></Settings>'
    $settings.Settings.BackupDirectory = $Directory.FullName
    $settings.Settings.DailyTime = $Time
    $temporarySettings = Join-Path $installDirectory ([Guid]::NewGuid().ToString() + '.tmp')
    try {
        $settings.Save($temporarySettings)
        if ([IO.File]::Exists($settingsPath)) {
            [IO.File]::Replace($temporarySettings, $settingsPath, [NullString]::Value)
        }
        else {
            [IO.File]::Move($temporarySettings, $settingsPath)
        }
    }
    finally {
        Remove-Item -LiteralPath $temporarySettings -Force -ErrorAction SilentlyContinue
    }
}

function Remove-CleanupInstallationDirectory {
    $target = [IO.Path]::GetFullPath($installDirectory)
    $expected = [IO.Path]::Combine([IO.Path]::GetFullPath($programFilesDirectory), 'BakCleanupService')
    if (-not [string]::Equals($target, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected installation path: $target"
    }

    $directory = Get-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
    if ($null -eq $directory) {
        return
    }
    if ($directory -isnot [IO.DirectoryInfo] -or
        ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The installation path is not a regular directory: $target"
    }

    Remove-CleanupDirectoryContents -Directory $directory
}

function Remove-CleanupDirectoryContents {
    param([IO.DirectoryInfo]$Directory)

    foreach ($entry in $Directory.EnumerateFileSystemInfos()) {
        $attributes = $entry.Attributes
        if ($entry -is [IO.DirectoryInfo]) {
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                $entry.Delete($false)
            }
            else {
                Remove-CleanupDirectoryContents -Directory $entry
            }
        }
        else {
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and
                ($attributes -band [IO.FileAttributes]::ReadOnly) -ne 0) {
                $entry.IsReadOnly = $false
            }
            $entry.Delete()
        }
    }
    $Directory.Delete($false)
}

function Show-UninstallConfirmation {
    param($Owner)

    $title = 'BAK Backup Cleanup Uninstall'
    try {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        $serviceExists = $null -ne $service
        if ($serviceExists) {
            $service.Dispose()
        }
        if (-not $serviceExists -and -not (Test-Path -LiteralPath $installDirectory)) {
            [void][Windows.Forms.MessageBox]::Show($Owner, 'The service is already uninstalled.',
                $title, 'OK', 'Information')
            return $true
        }

        $confirmation = [Windows.Forms.MessageBox]::Show($Owner,
            "Remove the service and permanently delete all files in the installation folder, including settings and logs?`r`n`r`n$installDirectory",
            $title, 'YesNo', 'Question', 'Button2')
        if ($confirmation -ne [Windows.Forms.DialogResult]::Yes) {
            return $false
        }

        if ($serviceExists) {
            Stop-Service -Name $serviceName -ErrorAction Stop
            $output = & (Join-Path $env:SystemRoot 'System32\sc.exe') delete $serviceName
            if ($LASTEXITCODE -ne 0) {
                throw ($output -join [Environment]::NewLine)
            }
        }
        Remove-CleanupInstallationDirectory
    }
    catch {
        [void][Windows.Forms.MessageBox]::Show($Owner, $_.Exception.Message,
            'Uninstall failed', 'OK', 'Error')
        return $false
    }

    [void][Windows.Forms.MessageBox]::Show($Owner,
        'The service and its installation folder have been removed.',
        $title, 'OK', 'Information')
    return $true
}

if ($Uninstall) {
    Add-Type -AssemblyName System.Windows.Forms
    $null = Show-UninstallConfirmation
    return
}

if ($Interactive) {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.Application]::EnableVisualStyles()
    $installer = $PSCommandPath
    $form = New-Object Windows.Forms.Form
    $form.Text = 'BAK Backup Cleanup Setup'
    $form.ClientSize = New-Object -TypeName System.Drawing.Size -ArgumentList 530, 230
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.AutoScaleMode = 'Dpi'

    $folderLabel = New-Object Windows.Forms.Label
    $folderLabel.Text = 'Backup folder'
    $folderLabel.AutoSize = $true
    $folderLabel.Location = New-Object -TypeName System.Drawing.Point -ArgumentList 16, 16

    $folderInput = New-Object Windows.Forms.TextBox
    $folderInput.SetBounds(16, 41, 396, 24)

    $browse = New-Object Windows.Forms.Button
    $browse.Text = 'Browse...'
    $browse.SetBounds(420, 39, 94, 28)
    $browse.Add_Click({
        $dialog = New-Object Windows.Forms.FolderBrowserDialog
        try {
            $dialog.Description = 'Select the folder containing your backup files.'
            $dialog.ShowNewFolderButton = $false
            if ([IO.Directory]::Exists($folderInput.Text)) {
                $dialog.SelectedPath = $folderInput.Text
            }
            if ($dialog.ShowDialog($form) -eq [Windows.Forms.DialogResult]::OK) {
                $folderInput.Text = $dialog.SelectedPath
            }
        }
        finally {
            $dialog.Dispose()
        }
    })

    $timeLabel = New-Object Windows.Forms.Label
    $timeLabel.Text = 'Daily cleanup time (server local time)'
    $timeLabel.AutoSize = $true
    $timeLabel.Location = New-Object -TypeName System.Drawing.Point -ArgumentList 16, 80

    $timeInput = New-Object Windows.Forms.DateTimePicker
    $timeInput.Format = 'Custom'
    $timeInput.CustomFormat = 'HH:mm'
    $timeInput.ShowUpDown = $true
    $timeInput.Value = [DateTime]::Today.AddHours(23)
    $timeInput.SetBounds(16, 104, 95, 24)

    $notice = New-Object Windows.Forms.Label
    $notice.Text = 'Installation starts the service and immediately deletes eligible .bak files older than 15 days.'
    $notice.SetBounds(16, 145, 498, 36)

    $installButton = New-Object Windows.Forms.Button
    $installButton.Text = 'Install'
    $installButton.SetBounds(320, 185, 94, 30)

    $uninstallButton = New-Object Windows.Forms.Button
    $uninstallButton.Text = 'Uninstall'
    $uninstallButton.SetBounds(220, 185, 94, 30)

    $cancel = New-Object Windows.Forms.Button
    $cancel.Text = 'Cancel'
    $cancel.DialogResult = 'Cancel'
    $cancel.SetBounds(420, 185, 94, 30)

    $installButton.Add_Click({
        if ($installed) {
            try {
                Start-Process -FilePath $installedSettingsExe -WindowStyle Normal
                $form.Close()
            }
            catch {
                [void][Windows.Forms.MessageBox]::Show($form, $_.Exception.Message,
                    'Could not open settings', 'OK', 'Error')
            }
            return
        }
        $path = $folderInput.Text.Trim().Trim('"')
        if (-not [IO.Directory]::Exists($path)) {
            [void][Windows.Forms.MessageBox]::Show($form, 'Select an existing backup folder.',
                $form.Text, 'OK', 'Warning')
            [void]$folderInput.Focus()
            return
        }

        $time = $timeInput.Value.ToString('HH:mm', [Globalization.CultureInfo]::InvariantCulture)
        $form.Enabled = $false
        $form.UseWaitCursor = $true
        try {
            & $installer -BackupDirectory $path -DailyTime $time | Out-Null
        }
        catch {
            $form.Enabled = $true
            [void][Windows.Forms.MessageBox]::Show($form, $_.Exception.Message,
                'Setup failed', 'OK', 'Error')
            return
        }
        finally {
            $form.UseWaitCursor = $false
            $form.Enabled = $true
        }

        [void][Windows.Forms.MessageBox]::Show($form,
            "Service installed. Daily cleanup is set for $time (server local time).`r`n`r`nTo change settings, open:`r`n$installedSettingsExe",
            $form.Text, 'OK', 'Information')
        $form.Close()
    })

    $uninstallButton.Add_Click({
        if (Show-UninstallConfirmation -Owner $form) {
            $form.Close()
        }
    })

    $existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    $installed = $null -ne $existingService
    $hasSettings = Test-Path -LiteralPath $settingsPath -PathType Leaf
    $canOpenSettings = $false
    if ($installed) {
        $existingService.Dispose()
        $registeredService = Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName" -Name ImagePath
        $registeredPath = $registeredService.ImagePath
        $canOpenSettings = $registeredPath -eq $binaryPath -and (Test-Path -LiteralPath $installedSettingsExe -PathType Leaf)
        $installButton.Text = 'Settings'
        $notice.Text = 'The service is installed. Use BakCleanupSettings.exe to change the backup folder or time.'
        if (-not $canOpenSettings) {
            $notice.Text = 'Reinstall this version of the service to add the standalone settings application.'
        }
    }
    if ($hasSettings -and -not $installed) {
        try {
            $settings = [xml](Get-Content -LiteralPath $settingsPath -Raw)
            $folderInput.Text = $settings.Settings.BackupDirectory
            $timeInput.Value = [DateTime]::Today.Add([DateTime]::ParseExact(
                $settings.Settings.DailyTime, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture).TimeOfDay)
        }
        catch {
            $notice.Text = 'Review both fields. Saving replaces the settings and immediately runs cleanup.'
        }
    }
    $installButton.Enabled = -not $installed -or $canOpenSettings
    $uninstallButton.Enabled = $installed -or (Test-Path -LiteralPath $installDirectory)
    $folderInput.Enabled = -not $installed
    $browse.Enabled = -not $installed
    $timeInput.Enabled = -not $installed

    $form.Controls.AddRange(@($folderLabel, $folderInput, $browse, $timeLabel, $timeInput,
        $notice, $uninstallButton, $installButton, $cancel))
    if ($installButton.Enabled) {
        $form.AcceptButton = $installButton
    }
    $form.CancelButton = $cancel
    try {
        [void]$form.ShowDialog()
    }
    finally {
        $form.Dispose()
    }
    return
}

$directory = Get-Item -LiteralPath $BackupDirectory
if ($directory -isnot [System.IO.DirectoryInfo]) {
    throw 'BackupDirectory must be a filesystem directory.'
}

if (-not $Preview) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run the installer from an elevated Windows PowerShell session.'
    }
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        throw "Service $serviceName already exists. Remove it before reinstalling."
    }
}

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The .NET Framework C# compiler was not found.'
}

$source = Join-Path $PSScriptRoot 'BakCleanupService.cs'
$sharedSettingsSource = Join-Path $PSScriptRoot 'CleanupSettings.cs'
$settingsSource = Join-Path $PSScriptRoot 'BakCleanupSettings.cs'
$settingsManifest = Join-Path $PSScriptRoot 'BakCleanupSettings.manifest'
$temporaryExe = Join-Path ([IO.Path]::GetTempPath()) ("$serviceName-" + [Guid]::NewGuid() + '.exe')
$temporarySettingsExe = Join-Path ([IO.Path]::GetTempPath()) ('BakCleanupSettings-' + [Guid]::NewGuid() + '.exe')
try {
    & $compiler /nologo /langversion:5 /target:exe /r:System.ServiceProcess.dll "/out:$temporaryExe" $source $sharedSettingsSource
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed with exit code $LASTEXITCODE."
    }

    if ($Preview) {
        & $temporaryExe --preview $directory.FullName
        if ($LASTEXITCODE -ne 0) {
            throw "Preview failed with exit code $LASTEXITCODE."
        }
        return
    }

    & $compiler /nologo /langversion:5 /target:winexe /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
        "/win32manifest:$settingsManifest" "/out:$temporarySettingsExe" $settingsSource $sharedSettingsSource
    if ($LASTEXITCODE -ne 0) {
        throw "Settings application compilation failed with exit code $LASTEXITCODE."
    }

    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    Write-CleanupSettings -Directory $directory -Time $DailyTime
    Copy-Item -LiteralPath $temporaryExe -Destination $installedExe -Force
    Copy-Item -LiteralPath $temporarySettingsExe -Destination $installedSettingsExe -Force

    if (-not [Diagnostics.EventLog]::SourceExists($serviceName)) {
        New-EventLog -LogName Application -Source $serviceName
    }

    New-Service -Name $serviceName -DisplayName 'BAK Backup Cleanup' -BinaryPathName $binaryPath `
        -StartupType Automatic | Out-Null
    Start-Service -Name $serviceName
    Get-Service -Name $serviceName
}
finally {
    Remove-Item -LiteralPath $temporaryExe -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporarySettingsExe -Force -ErrorAction SilentlyContinue
}
