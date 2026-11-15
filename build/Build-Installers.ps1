#requires -Version 4.0
param([string]$OutputDirectory = 'dist')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The 64-bit .NET Framework C# compiler is required.'
}

$payloads = @{
    BakArchiveService = @('Install-BakArchiveService.ps1','BakArchiveService.cs','ArchiveSettings.cs',
        'BakArchiveSettings.cs','BakArchiveSettings.manifest')
    BakCleanupService = @('Install-BakCleanupService.ps1','BakCleanupService.cs','CleanupSettings.cs',
        'BakCleanupSettings.cs','BakCleanupSettings.manifest')
}

foreach ($service in @('BakArchiveService', 'BakCleanupService')) {
    $folder = Join-Path $root $service
    $manifest = Join-Path $folder (($service -replace 'Service$', 'Settings') + '.manifest')
    $exe = Join-Path $output "$service-Setup.exe"
    $resources = foreach ($name in $payloads[$service]) {
        '/resource:{0},Payload.{1}' -f (Join-Path $folder $name), $name
    }
    & $compiler /nologo /langversion:5 /platform:x64 /target:winexe /optimize+ /r:System.Windows.Forms.dll `
        "/win32manifest:$manifest" "/out:$exe" @resources (Join-Path $PSScriptRoot 'SetupLauncher.cs')
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $service ($LASTEXITCODE)." }
    $exe
}
