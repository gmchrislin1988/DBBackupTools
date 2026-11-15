#requires -Version 4.0
[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Install')]
    [Parameter(Mandatory = $true, ParameterSetName = 'Preview')]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true, ParameterSetName = 'Install')]
    [string]$DestinationDirectory,

    [Parameter(ParameterSetName = 'Install')]
    [ValidatePattern('^(?:[01][0-9]|2[0-3]):[0-5][0-9]$')]
    [string]$DailyTime = '23:00',

    [Parameter(ParameterSetName = 'Install')]
    [ValidateSet('LowResource', 'FastZip', 'Ultra')]
    [string]$CompressionMode = 'LowResource',

    [Parameter(Mandatory = $true, ParameterSetName = 'Preview')]
    [switch]$Preview,

    [Parameter(Mandatory = $true, ParameterSetName = 'Interactive')]
    [switch]$Interactive,

    [Parameter(Mandatory = $true, ParameterSetName = 'Uninstall')]
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$serviceName = 'BakArchiveService'
$installDirectory = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) $serviceName
$settingsExe = Join-Path $installDirectory 'BakArchiveSettings.exe'

if ($Uninstall) {
    Add-Type -AssemblyName System.Windows.Forms
    $title = 'BAK Archive Service Uninstall'
    try {
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        $serviceExists = $null -ne $service
        if ($serviceExists) { $service.Dispose() }
        $directory = Get-Item -LiteralPath $installDirectory -Force -ErrorAction SilentlyContinue
        if ($null -ne $directory -and ($directory -isnot [IO.DirectoryInfo] -or
            ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw "The installation path is not a regular directory: $installDirectory"
        }

        $installedFiles = @('BakArchiveService.exe', 'BakArchiveSettings.exe', 'Settings.xml') | ForEach-Object {
            Join-Path $installDirectory $_
        } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }
        if (-not $serviceExists -and -not $installedFiles) {
            [void][Windows.Forms.MessageBox]::Show('The service is already uninstalled.',
                $title, 'OK', 'Information')
            return
        }

        $choice = [Windows.Forms.MessageBox]::Show(
            'Remove the service, its executable and settings? Source backups and archives will be kept.',
            $title, 'YesNo', 'Question', 'Button2')
        if ($choice -ne [Windows.Forms.DialogResult]::Yes) { return }

        if ($serviceExists) {
            Stop-Service -Name $serviceName
            $output = & (Join-Path $env:SystemRoot 'System32\sc.exe') delete $serviceName
            if ($LASTEXITCODE -ne 0) { throw ($output -join [Environment]::NewLine) }
        }
        foreach ($path in $installedFiles) {
            Remove-Item -LiteralPath $path -Force
        }
        if ($null -ne $directory -and -not (Get-ChildItem -LiteralPath $installDirectory -Force | Select-Object -First 1)) {
            $directory.Delete($false)
        }

        [void][Windows.Forms.MessageBox]::Show('The BAK Archive Service has been removed.',
            $title, 'OK', 'Information')
    }
    catch {
        [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, $title, 'OK', 'Error')
        exit 1
    }
    return
}

if ($Interactive) {
    trap {
        $detail = $_ | Out-String
        Write-Host $detail -ForegroundColor Red
        $null = Read-Host 'Press Enter to close this error window'
        exit 1
    }

    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.Application]::EnableVisualStyles()
    $installer = $PSCommandPath
    $form = New-Object Windows.Forms.Form
    $form.Text = 'BAK Archive Service Setup'
    $form.ClientSize = New-Object Drawing.Size -ArgumentList 546, 334
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.AutoScaleMode = 'Dpi'

    function Add-FolderInput {
        param([string]$Label, [int]$Top)

        $owner = $form
        $caption = New-Object Windows.Forms.Label
        $caption.Text = $Label
        $caption.AutoSize = $true
        $caption.Location = New-Object Drawing.Point -ArgumentList 16, $Top
        $inputBox = New-Object Windows.Forms.TextBox
        $inputBox.SetBounds(16, ($Top + 23), 410, 24)
        $browse = New-Object Windows.Forms.Button
        $browse.Text = 'Browse...'
        $browse.SetBounds(434, ($Top + 21), 96, 28)
        $browse.Add_Click({
            $dialog = New-Object Windows.Forms.FolderBrowserDialog
            try {
                $dialog.ShowNewFolderButton = $false
                if ([IO.Directory]::Exists($inputBox.Text)) {
                    $dialog.SelectedPath = $inputBox.Text
                }
                if ($dialog.ShowDialog($owner) -eq [Windows.Forms.DialogResult]::OK) {
                    $inputBox.Text = $dialog.SelectedPath
                }
            }
            finally {
                $dialog.Dispose()
            }
        }.GetNewClosure())
        $form.Controls.AddRange(@($caption, $inputBox, $browse))
        return $inputBox
    }

    $sourceInput = Add-FolderInput 'Source folder (.bak files)' 16
    $destinationInput = Add-FolderInput 'Destination folder (7z or ZIP archives)' 76

    $timeLabel = New-Object Windows.Forms.Label
    $timeLabel.Text = 'Daily run time (server local time)'
    $timeLabel.AutoSize = $true
    $timeLabel.Location = New-Object Drawing.Point -ArgumentList 16, 139
    $time = New-Object Windows.Forms.DateTimePicker
    $time.Format = 'Custom'
    $time.CustomFormat = 'HH:mm'
    $time.ShowUpDown = $true
    $time.Value = [DateTime]::Today.AddHours(23)
    $time.SetBounds(16, 162, 100, 24)

    $modeLabel = New-Object Windows.Forms.Label
    $modeLabel.Text = 'Compression mode'
    $modeLabel.AutoSize = $true
    $modeLabel.Location = New-Object Drawing.Point -ArgumentList 16, 195
    $modes = [ordered]@{
        'Low resource (7z)' = 'LowResource'
        'Fast ZIP' = 'FastZip'
        'Ultra (7z)' = 'Ultra'
    }
    $modeInput = New-Object Windows.Forms.ComboBox
    $modeInput.DropDownStyle = 'DropDownList'
    $modeInput.Items.AddRange(@($modes.Keys))
    $modeInput.SelectedIndex = 0
    $modeInput.SetBounds(16, 218, 215, 24)

    $notice = New-Object Windows.Forms.Label
    $notice.Text = 'The largest .bak from the latest backup date is archived. 7z modes use ZIP when 7-Zip is unavailable.'
    $notice.SetBounds(16, 251, 514, 36)
    $install = New-Object Windows.Forms.Button
    $install.Text = 'Install'
    $install.SetBounds(330, 289, 96, 30)
    $cancel = New-Object Windows.Forms.Button
    $cancel.Text = 'Cancel'
    $cancel.DialogResult = 'Cancel'
    $cancel.SetBounds(434, 289, 96, 30)

    $install.Add_Click({
        $schedule = $time.Value.ToString('HH:mm', [Globalization.CultureInfo]::InvariantCulture)
        $selectedMode = [string]$modeInput.SelectedItem
        try {
            $form.UseWaitCursor = $true
            & $installer -SourceDirectory $sourceInput.Text.Trim().Trim('"') `
                -DestinationDirectory $destinationInput.Text.Trim().Trim('"') -DailyTime $schedule `
                -CompressionMode $modes[$selectedMode] | Out-Null
        }
        catch {
            [void][Windows.Forms.MessageBox]::Show($form, $_.Exception.Message,
                'Installation failed', 'OK', 'Error')
            return
        }
        finally {
            $form.UseWaitCursor = $false
        }
        [void][Windows.Forms.MessageBox]::Show($form,
            "Service installed. Daily run time: $schedule. Compression mode: $selectedMode.`r`n`r`nTo change settings, open:`r`n$settingsExe",
            $form.Text, 'OK', 'Information')
        $form.Close()
    })

    $form.Controls.AddRange(@($timeLabel, $time, $modeLabel, $modeInput, $notice, $install, $cancel))
    $form.AcceptButton = $install
    $form.CancelButton = $cancel
    try {
        [void]$form.ShowDialog()
    }
    finally {
        $form.Dispose()
    }
    return
}

$source = Get-Item -LiteralPath $SourceDirectory
if ($source -isnot [IO.DirectoryInfo]) {
    throw 'SourceDirectory must be a filesystem folder.'
}

if (-not $Preview) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run the installer as administrator.'
    }
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
        throw 'BakArchiveService is already installed. See README.md to edit its settings.'
    }
    $destination = Get-Item -LiteralPath $DestinationDirectory
    if ($destination -isnot [IO.DirectoryInfo] -or
        $destination.FullName.TrimEnd('\') -eq $source.FullName.TrimEnd('\')) {
        throw 'DestinationDirectory must be a filesystem folder different from SourceDirectory.'
    }
}

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The .NET Framework C# compiler was not found.'
}

$temporaryExe = Join-Path ([IO.Path]::GetTempPath()) ("$serviceName-" + [Guid]::NewGuid() + '.exe')
$temporarySettingsExe = Join-Path ([IO.Path]::GetTempPath()) ('BakArchiveSettings-' + [Guid]::NewGuid() + '.exe')
$settingsSource = Join-Path $PSScriptRoot 'ArchiveSettings.cs'
try {
    & $compiler /nologo /langversion:5 /target:exe /r:System.ServiceProcess.dll /r:System.IO.Compression.dll `
        "/out:$temporaryExe" (Join-Path $PSScriptRoot 'BakArchiveService.cs') $settingsSource
    if ($LASTEXITCODE -ne 0) {
        throw "Compilation failed with exit code $LASTEXITCODE."
    }
    if ($Preview) {
        & $temporaryExe --preview $source.FullName
        if ($LASTEXITCODE -ne 0) { throw 'Preview failed.' }
        return
    }

    $manifest = Join-Path $PSScriptRoot 'BakArchiveSettings.manifest'
    & $compiler /nologo /langversion:5 /target:winexe /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll `
        /r:System.Drawing.dll "/win32manifest:$manifest" "/out:$temporarySettingsExe" `
        (Join-Path $PSScriptRoot 'BakArchiveSettings.cs') $settingsSource
    if ($LASTEXITCODE -ne 0) {
        throw "Settings application compilation failed with exit code $LASTEXITCODE."
    }

    New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
    $installedExe = Join-Path $installDirectory "$serviceName.exe"
    Copy-Item -LiteralPath $temporaryExe -Destination $installedExe -Force
    Copy-Item -LiteralPath $temporarySettingsExe -Destination $settingsExe -Force
    $settings = [xml]'<Settings><SourceDirectory/><DestinationDirectory/><DailyTime/><CompressionMode/></Settings>'
    $settings.Settings.SourceDirectory = $source.FullName
    $settings.Settings.DestinationDirectory = $destination.FullName
    $settings.Settings.DailyTime = $DailyTime
    $settings.Settings.CompressionMode = $CompressionMode
    $settings.Save((Join-Path $installDirectory 'Settings.xml'))

    if (-not [Diagnostics.EventLog]::SourceExists($serviceName)) {
        New-EventLog -LogName Application -Source $serviceName
    }
    New-Service -Name $serviceName -DisplayName 'BAK Archive Service' -BinaryPathName ('"{0}"' -f $installedExe) `
        -StartupType Automatic | Out-Null
    Start-Service -Name $serviceName
    Get-Service -Name $serviceName
}
finally {
    Remove-Item -LiteralPath $temporaryExe -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $temporarySettingsExe -Force -ErrorAction SilentlyContinue
}
