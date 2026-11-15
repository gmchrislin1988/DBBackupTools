# DBBackupTools

适用于 Windows Server 的备份归档与清理工具。两个工具分别注册为独立的 Windows 服务，可根据需要安装。

| 服务 | 用途 | 安装包 |
| --- | --- | --- |
| BAK 归档服务 | 按最后修改日期找到最新一天的备份，压缩当天最大的 `.bak` 并保存到目标目录，保留源文件。 | [下载归档服务](BakArchiveService/BakArchiveService-WindowsServer.zip) |
| BAK 清理服务 | 清理指定目录第一层中最后修改时间超过 15 天的 `.bak` 文件。 | [下载清理服务](BakCleanupService/BakCleanupService-WindowsServer.zip) |

## 安装与配置

1. 将所需安装包完整解压到一个文件夹。
2. 双击 `Install.cmd`，允许管理员权限请求。
3. 选择备份目录和每日运行时间。归档服务还需选择目标目录及压缩模式。默认运行时间为服务器本地时间 23:00。
4. 安装后，服务随 Windows 开机启动。可在安装目录双击 `BakArchiveSettings.exe` 或 `BakCleanupSettings.exe` 修改配置。

图形化安装需要 Windows 桌面环境。安装脚本兼容 Windows PowerShell 4.0，可在 Windows Server 2012 R2 上使用系统自带的 PowerShell。

## 归档与日志

归档服务支持快速 ZIP、低资源 7z 和 Ultra 7z。未安装 7-Zip 时，7z 模式自动使用内置 ZIP。新归档成功发布后，服务清理目标目录中前一天生成且符合服务命名规则的归档。

两个服务的操作日志均存放在各自安装目录下的 `LOGS` 文件夹，保留服务器本地日期的当天和前 6 天。卸载时运行对应安装包中的 `Uninstall.cmd`。

## 详细文档

- [归档服务使用说明](BakArchiveService/README.md)
- [归档性能测试报告](BakArchiveService/Performance-Test-Report.md)
- [清理服务使用说明](BakCleanupService/README.md)
