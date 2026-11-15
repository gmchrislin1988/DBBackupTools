# Windows Server BAK 清理服务

服务使用 Windows Server 的本机时间，默认每天 23:00 执行。Windows 开机时会自动启动服务，服务每次启动也会立即清理一次。若启动时间已到当天的计划任务时间，启动时的清理即算作当天的执行。

清理范围是指定目录的第一层。只有副档名为 `.bak`（不分大小写），且最後修改时间距执行时刻超过 15 天（360 小时）的文件才会永久删除。子目录和重新解析点会略过。

## 一键安装

在有桌面介面的 Windows Server 上，将安装 ZIP 的所有文件解压到同一个文件夹。双击 `Install.cmd` 并允许 Windows 的系统管理员权限提示。在安装窗口中按 **Browse...** 选择备份目录，设置服务器本机的每日执行时间（默认 `23:00`），然後按 **Install**。安装完成会立即启动服务并执行第一次清理。确认成功讯息後，安装窗口及启动时开启的命令窗口会自动关闭。

## 预览与命令列安装

在 Windows Server 上开启 Windows PowerShell 4.0 或更新的 Windows PowerShell，切换到安装档所在的文件夹并预览。预览只列出符合删除条件的文件。

```powershell
.\Install-BakCleanupService.ps1 -BackupDirectory 'D:\SQL Backups' -Preview
```

确认预览後，以系统管理员身分开启 Windows PowerShell，指定实际路径和每天的执行时间。安装完成会立刻启动服务并执行第一次清理。

```powershell
.\Install-BakCleanupService.ps1 -BackupDirectory 'D:\SQL Backups' -DailyTime '23:30'
Get-Service -Name BakCleanupService
```

Windows Server 2012 R2 可使用系统内建的 Windows PowerShell 4.0。若安装窗口启动时出错，命令窗口会保留错误讯息，详细资料也会写入提升权限後的 `%TEMP%\BakCleanupService-SetupError.log`。

服务默认以 `LocalSystem` 执行。请指定由管理员控制的备份目录，并确保服务帐户具有列出和删除文件的权限。

## 修改备份目录和时间

安装後，直接双击服务可执行文件旁的 `BakCleanupSettings.exe`，默认路径为 `C:\Program Files\BakCleanupService\BakCleanupSettings.exe`。允许系统管理员权限後，即可在独立窗口修改备份目录或每日执行时间。按 **Save** 後，服务会重新启动，并立即使用新设置清理一次。设置程序可直接从安装目录开启。

设置档是服务可执行文件旁的 `Settings.xml`，默认位於 `C:\Program Files\BakCleanupService\Settings.xml`。也可用系统管理员权限直接编辑，填写完整目录路径及 24 小时制时间，储存後执行 `Restart-Service -Name BakCleanupService` 套用。

```xml
<Settings>
  <BackupDirectory>D:\SQL Backups</BackupDirectory>
  <DailyTime>23:30</DailyTime>
</Settings>
```

已安装旧版服务时，请先卸载并安装新版一次，之後即可使用独立设置程序。

## 查看记录与移除

删除日志位於 `BakCleanupService.exe` 同一目录下的 `LOGS` 文件夹，默认为 `C:\Program Files\BakCleanupService\LOGS`。服务依服务器本机日期建立 `YYYY-MM-DD.log`，记录每笔成功删除的时间和文件完整路径，以及每次清理的删除总数和错误。每次执行会移除日期早於今天前 6 天的日志，保留今天及前 6 天。

```powershell
Get-Content (Join-Path $env:ProgramFiles "BakCleanupService\LOGS\$(Get-Date -Format yyyy-MM-dd).log")
```

若服务无法建立或写入日志，可在「事件检视器 → Windows 记录 → 应用程序」依来源 `BakCleanupService` 查看错误。已安装旧版的服务器需先移除旧服务，再用新版安装。

要卸载服务，双击解压目录中的 `Uninstall.cmd`，允许系统管理员权限并确认卸载。也可以从 `Install.cmd` 的窗口按 **Uninstall**。卸载会停止并移除 Windows 服务，再永久删除整个安装目录，包含可执行文件、`Settings.xml` 和 `LOGS`。如需保留设置或日志，请先复制到其他目录。若文件正在使用，关闭设置程序或其他正在使用该目录的程序後，再执行一次卸载。

在没有桌面介面的服务器上，可用系统管理员身分停止并移除服务，再从文件系统删除 `%ProgramFiles%\BakCleanupService` 安装目录。

```powershell
Stop-Service -Name BakCleanupService
sc.exe delete BakCleanupService
```
