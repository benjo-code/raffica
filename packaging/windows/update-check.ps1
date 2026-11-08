# Manual check of the Windows self-update through the real window (the owner's Windows PC: real desktop session).
# Called by .github/workflows/windows-update-check.yml, or by hand from the repository root:
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File packaging\windows\update-check.ps1
#
# What it does, in this order (packaging/README.md, section "Manual update check", step 3):
#   1. Checks the machine: a desktop session, dotnet, the C++ build tools of Native AOT, and that Raffica PDF is not
#      installed (the test installs and uninstalls it below HKCU, like installer-smoke).
#   2. Makes a throw-away test key (in a folder of its own below %TEMP%, removed at the end), builds the CLI and the PDF app twice as
#      the TEST BUILD of the updater (versions OldVersion and NewVersion, default 0.0.1 and 0.0.2), builds both installers
#      and starts the test server of tools/update-testserver.cs on 127.0.0.1 with the new installer (signed with the test key).
#   3. Installs the OLD version silently into a test folder and starts it on the real desktop with a profile of its own
#      (APPDATA and LOCALAPPDATA point below the work folder, so your own settings stay untouched), and WAITS for you:
#      "Update available" must show in the title bar; click it, then "Install" (if nothing shows after a minute:
#      Ctrl+K, type "Check for updates", Enter). The app closes, the helper runs the new installer and starts the new version.
#   4. Checks what the update did: RafficaPdf.exe reports the new version, the staging folder is gone, the new version is
#      running, the uninstaller is still there. You check by eye that the new version is open (Ctrl+K, type "About", Enter:
#      the card shows the version) and the document is back; closing the window ends the run.
#   5. Always, also after a failure: ends the processes of the test folders, uninstalls (registry and files), removes the work
#      folder with the test key.
#
# SECURITY: this runs repository code with the rights of the user who started the runner. It uses no secret and no network
# except the downloads of Inno Setup (pinned, SHA-256 checked) and of the NuGet packages. The server answers on 127.0.0.1
# only; the test key is useless against any release (a release build trusts only its own keys).
param(
    [string]$OldVersion = '0.0.1',
    [string]$NewVersion = '0.0.2',
    [int]$WaitMinutes = 20,
    [int]$HoldMinutes = 5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

function Write-Note([string]$Text) { Write-Host "== $Text" }
function Stop-Run([string]$Text) { Write-Host "::error title=Windows update check::$Text"; throw $Text }

# Runs a native program and stops when it fails (a failing native program does not stop PowerShell by itself).
function Invoke-Native([string]$File, [string[]]$Arguments) {
    & $File @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { Stop-Run "$File $($Arguments -join ' ') ended with exit code $LASTEXITCODE" }
}

if ($OldVersion -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or $NewVersion -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or $OldVersion -eq $NewVersion) {
    Stop-Run 'OldVersion and NewVersion must be two different versions X.Y.Z'
}
if ($WaitMinutes -lt 1 -or $WaitMinutes -gt 120 -or $HoldMinutes -lt 0 -or $HoldMinutes -gt 120) { Stop-Run 'WaitMinutes must be 1 to 120 and HoldMinutes 0 to 120' }

Write-Note 'Checking the machine'
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitOperatingSystem) { Stop-Run 'needs 64-bit Windows' }
if (-not [Environment]::UserInteractive -or (Get-Process -Id $PID).SessionId -eq 0) {
    Stop-Run 'no desktop session: start the runner (run.cmd) in the logged-in desktop of the user, not as a service'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { Stop-Run 'dotnet is missing (the .NET SDK of global.json)' }
if (-not (Test-Path global.json) -or -not (Test-Path tools\package.cs)) { Stop-Run 'run it from the repository root' }
$vsInstaller = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
if (Test-Path $vsInstaller) { $env:PATH = "$vsInstaller;$env:PATH" }
if (-not (Get-Command vswhere -ErrorAction SilentlyContinue)) { Stop-Run 'vswhere not found: install the Visual Studio Build Tools (Native AOT needs the C++ tools)' }
if (-not (vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)) {
    Stop-Run 'the Visual C++ build tools are not installed (Native AOT needs them)'
}
# The build refuses these as environment variables (Directory.Build.props); better to say so before a long build.
foreach ($name in 'RafficaUpdateTestBuild', 'RafficaUpdateTestPort', 'RafficaUpdateTestKey') {
    if ([Environment]::GetEnvironmentVariable($name)) { Stop-Run "$name is set as an environment variable; the test build takes it only as a -p: property" }
}
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + (Select-String -Path packaging\windows\raffica-pdf.iss -Pattern '^AppId=\{\{([0-9A-Fa-f-]{36})\}' | Select-Object -First 1).Matches[0].Groups[1].Value + '}_is1'
foreach ($entry in @(@('HKCU:', 'Software\Classes\Raffica.PDF'), @('HKCU:', $uninstallKey), @('HKLM:', $uninstallKey))) {
    if (Test-Path (Join-Path $entry[0] $entry[1])) {
        Stop-Run "Raffica PDF is already installed or registered ($($entry[0])\$($entry[1])); uninstall it first, the test would remove it"
    }
}
$repo = (Get-Location).Path
dotnet --version

$work = Join-Path ([IO.Path]::GetTempPath()) ('raffica-update-check-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work | Out-Null
$installDir = Join-Path $work 'Raffica PDF'
$roaming = Join-Path $work 'Roaming'
$local = Join-Path $work 'Local'
$exe = Join-Path $installDir 'RafficaPdf.exe'
$server = $null
$installedOld = $false
$failed = $false

# Processes of the app that run from one of the test folders (the installed program or a copy of the helper).
function Get-TestProcesses {
    Get-Process -Name RafficaPdf -ErrorAction SilentlyContinue | Where-Object {
        try { $_.MainModule.FileName -like "$work\*" } catch { $false }
    }
}

function Get-ProductVersion { if (Test-Path $exe) { [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion } else { $null } }

# What is running and what is on disk, for a failure that is hard to understand afterwards.
function Show-Diagnostics {
    Write-Host '--- diagnostics'
    Write-Host "product version of the installed exe: $(Get-ProductVersion)"
    Write-Host 'processes of the app from the test folders:'
    Get-TestProcesses | ForEach-Object { Write-Host "  $($_.Id) $($_.MainModule.FileName) (running since $($_.StartTime))" }
    Write-Host 'staging folder of the app:'
    Get-ChildItem (Join-Path $roaming 'Raffica\updates') -Recurse -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $($_.FullName)" }
    Write-Host 'logs of the installers (Setup Log *.txt in the work folder and in %TEMP%):'
    Get-ChildItem $work, $env:TEMP -Filter 'Setup Log *.txt' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 3 | ForEach-Object {
        Write-Host "  $($_.FullName)"
        Get-Content $_.FullName -Tail 12 | ForEach-Object { Write-Host "    $_" }
    }
    Write-Host '--- end of diagnostics'
}

try {
    # A free port on the loopback interface.
    $port = $null
    foreach ($try in 1..50) {
        $candidate = Get-Random -Minimum 20000 -Maximum 60000
        try {
            $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $candidate)
            $listener.Start(); $listener.Stop()
            $port = $candidate
            break
        } catch { }
    }
    if (-not $port) { Stop-Run 'no free port found' }

    Write-Note "Test key and test builds ($OldVersion, $NewVersion), port $port"
    $key = Join-Path $work 'test.key'
    $keygen = Join-Path $work 'keygen.out'
    dotnet run tools/package.cs -- update-keygen --id test-1 --private-out $key | Set-Content -Path $keygen -Encoding utf8
    if ($LASTEXITCODE -ne 0) { Stop-Run 'update-keygen failed' }
    $pub = [regex]::Match((Get-Content $keygen -Raw), 'FromBase64String\("([^"]+)"').Groups[1].Value
    if (-not $pub) { Stop-Run 'could not read the public test key from the output of update-keygen' }

    $testProps = @('-p:RafficaUpdateTestBuild=true', "-p:RafficaUpdateTestPort=$port", "-p:RafficaUpdateTestKey=$pub")
    Invoke-Native dotnet (@('publish', 'src/Raffica.Cli', '-c', 'Release', '-r', 'win-x64', "-p:Version=$OldVersion", '-o', "$work\cli"))
    Invoke-Native dotnet (@('publish', 'src/Raffica.Pdf', '-c', 'Release', '-r', 'win-x64') + $testProps + @("-p:Version=$OldVersion", '-o', "$work\app-old"))
    Invoke-Native dotnet (@('publish', 'src/Raffica.Pdf', '-c', 'Release', '-r', 'win-x64') + $testProps + @("-p:Version=$NewVersion", '-o', "$work\app-new"))

    Write-Note 'Installers'
    Invoke-Native dotnet @('run', 'tools/package.cs', '--', 'inno-install')
    New-Item -ItemType Directory -Path "$work\release-old", "$work\release-new" | Out-Null
    foreach ($v in @(@('old', $OldVersion), @('new', $NewVersion))) {
        Invoke-Native dotnet @('run', 'tools/package.cs', '--', 'layout', '--rid', 'win-x64', '--app', "$work\app-$($v[0])", '--cli', "$work\cli", '--out', "$work\layout-$($v[0])")
        Invoke-Native dotnet @('run', 'tools/package.cs', '--', 'installer', '--layout', "$work\layout-$($v[0])", '--version', $v[1], '--out', "$work\release-$($v[0])\RafficaPdf-$($v[1])-win-x64-setup.exe")
    }
    $oldSetup = "$work\release-old\RafficaPdf-$OldVersion-win-x64-setup.exe"
    $newSetup = "$work\release-new\RafficaPdf-$NewVersion-win-x64-setup.exe"

    Write-Note "Test server on 127.0.0.1:$port with version $NewVersion"
    Invoke-Native dotnet @('run', 'tools/update-testserver.cs', '--', 'prepare', '--version', $NewVersion, '--key', $key, '--out', "$work\server", $newSetup)
    $server = Start-Process dotnet -ArgumentList @('run', 'tools/update-testserver.cs', '--', 'serve', '--root', "$work\server", '--port', $port) `
        -PassThru -WindowStyle Hidden -RedirectStandardOutput "$work\server.log" -RedirectStandardError "$work\server.err.log"
    $ready = $false
    foreach ($try in 1..90) {
        try {
            if ((Invoke-WebRequest "http://127.0.0.1:$port/releases/latest" -UseBasicParsing -TimeoutSec 3).Content -like "*v$NewVersion*") { $ready = $true; break }
        } catch { }
        if ($server.HasExited) { Get-Content "$work\server.log", "$work\server.err.log" -ErrorAction SilentlyContinue | Out-Host; Stop-Run 'the test server ended' }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { Stop-Run "the test server does not answer on port $port" }

    Write-Note 'Installing the old version'
    $install = Start-Process $oldSetup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$installDir`"", "/LOG=`"$work\install-old.log`"") -Wait -PassThru
    $installedOld = $true
    if ($install.ExitCode -ne 0 -or -not (Test-Path $exe)) { Stop-Run "installing the old version failed (exit $($install.ExitCode); log $work\install-old.log)" }
    $installedVersion = Get-ProductVersion
    if (-not $installedVersion.StartsWith($OldVersion)) { Stop-Run "the installed exe reports $installedVersion instead of $OldVersion" }
    Write-Host "Installed: $exe ($installedVersion)"

    # A profile of its own: the helper and the restarted version take %APPDATA% and %LOCALAPPDATA% from the environment.
    New-Item -ItemType Directory -Path (Join-Path $roaming 'Raffica'), $local | Out-Null
    $psi = New-Object Diagnostics.ProcessStartInfo $exe
    $psi.UseShellExecute = $false
    $psi.WorkingDirectory = $installDir
    $psi.Arguments = '"' + (Join-Path $repo 'testdata\pdfs\text-basic.pdf') + '"'
    $psi.EnvironmentVariables['APPDATA'] = $roaming
    $psi.EnvironmentVariables['LOCALAPPDATA'] = $local
    $app = [Diagnostics.Process]::Start($psi)
    Start-Sleep -Seconds 15
    if ($app.HasExited) { Stop-Run "the old version did not stay open (exit $($app.ExitCode))" }
    Write-Host "The old version ($OldVersion) is open (process $($app.Id))"

    $message = "Raffica update check: in the Raffica window ($OldVersion) the title bar shows 'Update available'. Click it, then 'Install'. If it does not show within a minute: Ctrl+K, type 'Check for updates', Enter. Afterwards: Ctrl+K, type 'About', Enter shows the version; the PDF must be open again."
    Write-Host "::notice title=Your turn::$message"
    Write-Host $message
    try {
        [void][Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms')
        $balloon = New-Object Windows.Forms.NotifyIcon
        $balloon.Icon = [Drawing.SystemIcons]::Information
        $balloon.Visible = $true
        $balloon.ShowBalloonTip(15000, 'Raffica update check', "Click 'Update available', then 'Install' in the Raffica window.", 'Info')
    } catch { }

    Write-Note "Waiting for the update (up to $WaitMinutes minutes)"
    $deadline = (Get-Date).AddMinutes($WaitMinutes)
    $updated = $false
    $closedSince = $null
    while ((Get-Date) -lt $deadline) {
        if ((Get-ProductVersion) -like "$NewVersion*") { $updated = $true; break }
        # The old program ends first and the helper runs the installer for a while: that is normal. Longer than three minutes
        # with nothing of the app running and the version unchanged is not.
        if (@(Get-TestProcesses).Count -eq 0) {
            if (-not $closedSince) { $closedSince = Get-Date }
            if (((Get-Date) - $closedSince).TotalSeconds -gt 180) {
                Show-Diagnostics
                Stop-Run 'the app was closed (or crashed) and the version did not change within three minutes'
            }
        } else { $closedSince = $null }
        Start-Sleep -Seconds 2
    }
    if (-not $updated) { Show-Diagnostics; Stop-Run "no update within $WaitMinutes minutes: the installed version is still $(Get-ProductVersion)" }

    Write-Note 'What the update did'
    $failures = 0
    function Test-Check([string]$What, [bool]$Ok) { if ($Ok) { Write-Host "ok   $What" } else { Write-Host "FAIL $What"; $script:failures++ } }
    Start-Sleep -Seconds 3
    Test-Check "RafficaPdf.exe reports the new version ($(Get-ProductVersion))" ((Get-ProductVersion) -like "$NewVersion*")
    Test-Check 'the uninstaller is still there' (Test-Path (Join-Path $installDir 'unins000.exe'))
    Test-Check 'the staging folder of the update is gone' (-not (Test-Path (Join-Path $roaming "Raffica\updates\$NewVersion")))
    $restarted = $false
    foreach ($try in 1..60) {
        if (@(Get-TestProcesses | Where-Object { $_.MainModule.FileName -eq $exe }).Count -gt 0) { $restarted = $true; break }
        Start-Sleep -Seconds 1
    }
    Test-Check 'the new version is running from the installed folder' $restarted
    if (-not $restarted) { Show-Diagnostics }
    if ($failures -gt 0) { Stop-Run "$failures check(s) failed (see above)" }

    Write-Host ''
    Write-Host "All automatic checks passed. Look at the screen now: Ctrl+K, type About, Enter: the card shows $NewVersion; and the"
    Write-Host "document (text-basic.pdf) is open again. Close the Raffica window to end the run (it is closed after $HoldMinutes minutes in any case)."
    $holdDeadline = (Get-Date).AddMinutes($HoldMinutes)
    while ((Get-Date) -lt $holdDeadline -and @(Get-TestProcesses | Where-Object { $_.MainModule.FileName -eq $exe }).Count -gt 0) { Start-Sleep -Seconds 2 }
    Write-Note 'Done'
} catch {
    $failed = $true
    throw
} finally {
    Write-Note 'Cleaning up'
    Get-TestProcesses | ForEach-Object { try { $_.Kill(); [void]$_.WaitForExit(30000) } catch { } }
    if ($server -and -not $server.HasExited) { & taskkill /T /F /PID $server.Id | Out-Null }
    if ($installedOld) {
        $uninstaller = Join-Path $installDir 'unins000.exe'
        if (Test-Path $uninstaller) {
            $un = Start-Process $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$work\uninstall.log`"") -Wait -PassThru
            if ($un.ExitCode -ne 0) { Write-Host "::warning title=Windows update check::the uninstaller ended with exit $($un.ExitCode) (log $work\uninstall.log); look at HKCU for leftovers" }
        }
        foreach ($entry in @(@('HKCU:', 'Software\Classes\Raffica.PDF'), @('HKCU:', $uninstallKey))) {
            if (Test-Path (Join-Path $entry[0] $entry[1])) { Write-Host "::warning title=Windows update check::leftover after the uninstall: $($entry[0])\$($entry[1])" }
        }
    }
    # The throw-away private key goes with the work folder. After a failure the logs would be useful, but the key is not worth keeping.
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
