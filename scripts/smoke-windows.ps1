param([Parameter(Mandatory)][string] $Path)
$ErrorActionPreference = 'Stop'
$launcher = Start-Process -FilePath (Resolve-Path $Path) -PassThru
try {
    $root = Join-Path $env:LOCALAPPDATA 'ScrcpyStudio/runtime'
    $backend = $null
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        Start-Sleep -Milliseconds 500
        $launcher.Refresh()
        if ($launcher.HasExited) {
            throw "Launcher exited during startup ($($launcher.ExitCode))."
        }
        if (Test-Path $root) {
            $extract = Get-ChildItem $root -Directory | Where-Object Name -Match '^[a-f0-9]{64}$' | Select-Object -First 1
            if ($extract) { $backend = Get-Item (Join-Path $extract.FullName 'scrcpy.exe') }
            if ($backend -and $launcher.MainWindowHandle -ne 0) { break }
        }
    }
    if (!$backend -or $launcher.MainWindowHandle -eq 0) {
        throw 'The launcher window and extracted scrcpy backend must both be present.'
    }

    $env:ADB = Join-Path $backend.DirectoryName 'adb.exe'
    if (!(Test-Path (Join-Path $backend.DirectoryName 'scrcpy-server'))) {
        throw 'The extracted Android server is missing.'
    }
    if (!(Test-Path (Join-Path $backend.DirectoryName 'LICENSES/adb-notices.txt'))) {
        throw 'The bundled notices are missing.'
    }
    $version = & $backend.FullName --version | Out-String
    if ($LASTEXITCODE -ne 0 -or $version -notmatch 'scrcpy 5\.0\.1' -or $version -notmatch 'libusb') {
        throw "Unexpected scrcpy version output: $version"
    }
    $help = & $backend.FullName --help | Out-String
    if ($LASTEXITCODE -ne 0) { throw 'scrcpy --help failed.' }
    $metadata = Get-Content -Raw (Join-Path $PSScriptRoot '../data/options.json') | ConvertFrom-Json
    foreach ($group in $metadata.PSObject.Properties) {
        foreach ($entry in $group.Value) {
            $key = $entry[0]
            $option = if ($group.Name -eq '运行环境' -or $key.StartsWith('-')) { $key } else { "--$key" }
            if (!$help.Contains($option)) { throw "Missing CLI option $option" }
        }
    }
    & $env:ADB version
    if ($LASTEXITCODE -ne 0) { throw 'adb version failed.' }
    & $env:ADB devices -l
    if ($LASTEXITCODE -ne 0) { throw 'adb devices failed.' }
    Write-Host 'Windows launcher, native scrcpy and bundled ADB started successfully.'
}
finally {
    $launcher.Refresh()
    if (!$launcher.HasExited -and $launcher.CloseMainWindow()) {
        if (!$launcher.WaitForExit(8000)) { $launcher.Kill() }
    } elseif (!$launcher.HasExited) {
        $launcher.Kill()
    }
}
