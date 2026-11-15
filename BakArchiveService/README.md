# Windows Server BAK 压缩服务

## 安装

1. 把安装包的所有文件解压到同一个文件夹。
2. 双击 `Install.cmd`，允许系统管理员权限。
3. 选择已存在的来源目录和目标目录，设置服务器本机的每日执行时间，并在 **Compression mode** 选择 **Low resource (7z)**、**Fast ZIP** 或 **Ultra (7z)**，再按 **Install**。默认为 `23:00` 和 **Low resource (7z)**。

7-Zip 为可选项目。服务器没有安装 7-Zip 时，可直接安装服务并使用内建 ZIP 压缩。

服务名称为 `BakArchiveService`，设置为随 Windows 开机自动启动。若服务启动时已过当天计划任务时间，会补跑一次。安装器使用 Windows PowerShell 4.0 及 .NET Framework，包含 Windows Server 2012 R2 的内建环境。

服务以 `LocalSystem` 执行。若目标为网络共用目录，请填写 UNC 路径并在共用及文件系统上授予服务器电脑帐户所需权限。服务需要读取来源文件，并能在目标目录建立、重新命名和删除暂存档。

## 备份挑选规则

- 只查看来源目录第一层的 `.bak` 文件，依服务器本机时区的最後修改日期找出最新一天。
- 挑选该日大小最大的文件。大小相同时优先选最後修改时间较晚的文件，时间也相同时依档名排序。
- 有 7-Zip 时默认使用 LZMA2 低资源模式（`-mx=3 -md=16m -mmt=1`），产生例如 `BakArchive-20261112-Full.7z`。16 MiB 是字典大小，实际程序记忆体还包含其他开销。7-Zip 程序会尝试以低於正常的 CPU 调度优先级执行。未安装 7-Zip 时使用内建 ZIP Deflate，产生 `BakArchive-20261112-Full.zip`。来源 `.bak` 保留。
- 新压缩档先写入目标目录的暂存档，完成後才放到正式路径。`.7z` 另会用 7-Zip 验证完整性。若同名档已存在，服务会移开旧档、放入新档，再删除旧档。若放入失败，会尝试恢复旧档。
- 新档放入目标目录後，服务会删除同一目录内以 `BakArchive-` 开头且日期为前一天的 `.7z` 或 `.zip`。
- 遇到文件仍在写入或其他执行错误时，服务每隔 5 分钟重试，当天最多尝试 3 次。

## 预览与命令列安装

```powershell
.\Install-BakArchiveService.ps1 -SourceDirectory 'D:\SQL Backups' -Preview
.\Install-BakArchiveService.ps1 -SourceDirectory 'D:\SQL Backups' -DestinationDirectory 'E:\BAK Archives' -DailyTime '23:30' -CompressionMode FastZip
```

预览只列出本次会挑选的文件。实际安装需以系统管理员身分执行。

## 修改设置与查看记录

安装後直接双击 `C:\Program Files\BakArchiveService\BakArchiveSettings.exe`，允许系统管理员权限後，可修改来源目录、目标目录和每日执行时间。按 **Save** 会储存设置并重新启动服务。若已过新设置的当日执行时间，服务会补跑一次。旧版需先卸载并重新安装，才会安装此设置程序。

设置窗口的 **Compression mode** 可选 **Low resource (7z)**（默认）、**Fast ZIP** 或 **Ultra (7z)**。**Fast ZIP** 即使安装了 7-Zip 也会使用内建 ZIP 的快速等级。**Ultra (7z)**（`-mx=9 -mmt=2`）可能使用较多 CPU 和记忆体。

也可用系统管理员权限编辑同一目录的 `Settings.xml`。其中 `SourceDirectory` 是来源目录，`DestinationDirectory` 是目标目录，`DailyTime` 采 24 小时制，`CompressionMode` 可设为 `LowResource`、`FastZip` 或 `Ultra`。储存後执行 `Restart-Service -Name BakArchiveService`。

操作日志位於服务可执行文件旁的 `LOGS` 文件夹，默认为 `C:\Program Files\BakArchiveService\LOGS`。每天建立一个 `YYYY-MM-DD.log`，记录备份挑选、压缩前後大小、前一天压缩档的删除及重试错误。服务每次执行时清理过期日志，保留服务器本机日期的今天和前 6 天。

若操作日志无法写入，可在 Windows 事件检视器的「应用程序」记录中，以来源 `BakArchiveService` 查看写入错误。

## 卸载

先关闭设置程序，再双击解压目录中的 `Uninstall.cmd`，允许系统管理员权限後确认卸载。程序会停止并移除 `BakArchiveService`，删除服务可执行文件、设置程序及设置档，并在安装目录已空时移除该目录。来源 `.bak` 和目标压缩档会保留。

也可用系统管理员身分执行以下命令停止并移除服务注册。

```powershell
Stop-Service -Name BakArchiveService
sc.exe delete BakArchiveService
```
