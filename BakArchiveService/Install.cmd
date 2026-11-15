@echo off
setlocal
set "BAK_ARCHIVE_INSTALLER=%~dp0Install-BakArchiveService.ps1"
if not exist "%BAK_ARCHIVE_INSTALLER%" (
    echo Install-BakArchiveService.ps1 must be in the same folder as Install.cmd.
    pause
    exit /b 1
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command "$ErrorActionPreference = 'Stop'; try { $script = '{0}{1}{0}' -f [char]34, $env:BAK_ARCHIVE_INSTALLER; $setup = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -Verb RunAs -Wait -PassThru -ArgumentList ('-NoProfile -STA -ExecutionPolicy Bypass -File ' + $script + ' -Interactive'); exit $setup.ExitCode } catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }"
if errorlevel 1 (
    echo Setup failed. Exit code: %errorlevel%.
    pause
    exit /b 1
)
exit /b 0
