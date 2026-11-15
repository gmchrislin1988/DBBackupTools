@echo off
setlocal
set "BAK_CLEANUP_INSTALLER=%~dp0Install-BakCleanupService.ps1"
if not exist "%BAK_CLEANUP_INSTALLER%" (
    echo Install-BakCleanupService.ps1 must be in the same folder as Uninstall.cmd.
    pause
    exit /b 1
)

"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command "$ErrorActionPreference = 'Stop'; try { $installer = '{0}{1}{0}' -f [char]34, $env:BAK_CLEANUP_INSTALLER; $process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -Verb RunAs -Wait -PassThru -ArgumentList ('-NoProfile -STA -ExecutionPolicy Bypass -File ' + $installer + ' -Uninstall'); exit $process.ExitCode } catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }"
if errorlevel 1 (
    echo Uninstall did not start or exited with an error.
    pause
    exit /b 1
)
exit /b 0
