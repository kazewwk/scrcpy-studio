param(
    [Parameter(Mandatory)][string] $Path,
    # Receives a capture of the real window and a rendering of every page in both themes.
    [string] $Pictures
)
$ErrorActionPreference = 'Stop'
$exe = (Resolve-Path $Path).Path
if ($Pictures) { $Pictures = (New-Item -ItemType Directory -Force $Pictures).FullName }

$launcher = Start-Process -FilePath $exe -PassThru
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
    foreach ($notice in 'adb-notices.txt', 'Lucide.txt') {
        if (!(Test-Path (Join-Path $backend.DirectoryName "LICENSES/$notice"))) {
            throw "The bundled notice $notice is missing."
        }
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

    if ($Pictures) {
        Add-Type -AssemblyName System.Drawing, System.Windows.Forms
        $screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bitmap = [System.Drawing.Bitmap]::new($screen.Width, $screen.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
        $bitmap.Save((Join-Path $Pictures 'desktop.png'))
        $graphics.Dispose()
        $bitmap.Dispose()
    }
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

if ($Pictures) {
    $render = Start-Process -FilePath $exe -ArgumentList '--snapshot', "`"$Pictures`"" -PassThru
    if (!$render.WaitForExit(180000)) {
        $render.Kill()
        throw 'Rendering the interface timed out.'
    }
    $pages = @(Get-ChildItem $Pictures -Filter '*-*.png')
    if ($render.ExitCode -ne 0 -or $pages.Count -ne 12) {
        $detail = Join-Path $Pictures 'error.txt'
        $reason = if (Test-Path $detail) { Get-Content -Raw $detail } else { "$($pages.Count) pages" }
        throw "The interface did not render completely (exit $($render.ExitCode)). $reason"
    }
    Write-Host "Rendered $($pages.Count) pages of the interface."
}
