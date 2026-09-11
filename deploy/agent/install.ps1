<#
.SYNOPSIS
Installs the ServerMonitor agent on this Windows machine.

.DESCRIPTION
Downloads the agent - one self-contained executable, so no .NET needs installing - puts it under
Program Files and registers a scheduled task that starts it with Windows. Running the script again
upgrades the agent in place and keeps the machine's identity, agent-state.json.

Run it in PowerShell opened as Administrator. The dashboard's "Add a machine" page shows the whole
command with the address and the token filled in:

    & ([scriptblock]::Create((Invoke-RestMethod 'https://raw.githubusercontent.com/SoftGene/ServerMonitor/master/deploy/agent/install.ps1'))) -Url 'http://192.168.1.20:7212' -Token '<token>'

.PARAMETER Url
Address of the API, such as http://192.168.1.20:7212.

.PARAMETER Token
The enrollment token: ENROLLMENT_TOKEN in the server's .env.

.PARAMETER Version
A particular release to install, such as v1.2.0, rather than the latest.

.PARAMETER PackagePath
An agent archive already on this machine, installed instead of downloading one.

.PARAMETER InstallDir
Where the agent goes.

.PARAMETER Uninstall
Stops the agent and removes it from this machine.
#>
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'An interactive installer: its progress is for the person watching, not for a pipeline.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'The script is the unit of work; -WhatIf on its inner functions would promise a dry run the installer does not have.')]
[CmdletBinding()]
param(
    [string] $Url,
    [string] $Token,
    [ValidatePattern('^(latest|v[0-9][0-9A-Za-z.-]*)$')]
    [string] $Version = 'latest',
    [string] $PackagePath,
    [string] $InstallDir = (Join-Path $env:ProgramFiles 'ServerMonitor\Agent'),
    [switch] $Uninstall
)

# The script runs as a script block inside the caller's own session, so these stay in its scope and
# do not outlive it there.
Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'
# Windows PowerShell redraws a progress bar for every buffer it downloads, which slows a download of
# tens of megabytes to minutes.
$ProgressPreference = 'SilentlyContinue'

$Repo = 'SoftGene/ServerMonitor'
$TaskName = 'ServerMonitor Agent'
$ExeName = 'ServerMonitor.Agent.exe'
$Asset = 'servermonitor-agent-win-x64.zip'
$ConfigurationName = 'appsettings.Production.json'
# What the agent exits with when its configuration cannot work: a token the server refuses, or none.
$ConfigurationErrorExitCode = 78

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-Step([string] $Message) {
    Write-Host "==> $Message"
}

function Stop-Agent([string] $InstallDir) {
    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $TaskName
    }

    # Stopping the task ends the process, though not always before the next line runs, and an
    # executable that is still running cannot be replaced.
    $exe = Join-Path $InstallDir $ExeName
    foreach ($process in @(Get-Process -Name 'ServerMonitor.Agent' -ErrorAction SilentlyContinue)) {
        if ($process.Path -eq $exe) {
            try {
                $process.Kill()
            }
            catch [InvalidOperationException] {
                Write-Verbose 'The agent exited on its own before it could be stopped.'
            }
            [void] $process.WaitForExit(10000)
        }
    }
}

# The installer's own configuration file. appsettings.json ships in the package and is replaced by
# every upgrade; this file layers on top of it and is in no package, so the address and the token
# survive one.
function Read-AgentConfiguration([string] $Path) {
    if (Test-Path -LiteralPath $Path) {
        $configuration = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    else {
        $configuration = New-Object PSObject
    }

    if (-not $configuration.PSObject.Properties['Agent']) {
        $configuration | Add-Member -NotePropertyName Agent -NotePropertyValue (New-Object PSObject)
    }

    return $configuration
}

function Get-AgentSetting($Configuration, [string] $Name) {
    $property = $Configuration.Agent.PSObject.Properties[$Name]
    if ($property -and $property.Value) {
        return [string] $property.Value
    }
    return ''
}

function Write-AgentConfiguration($Configuration, [string] $Path) {
    # Without a byte order mark, which Windows PowerShell's own cmdlets would write.
    $json = $Configuration | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding $false))
}

function Confirm-Checksum([string] $File, [string] $SumsFile) {
    $name = Split-Path -Leaf $File
    $expected = $null

    foreach ($line in Get-Content -LiteralPath $SumsFile) {
        if ($line -match '^([0-9a-fA-F]{64}) [ *](.+)$' -and $Matches[2] -eq $name) {
            $expected = $Matches[1]
        }
    }

    if (-not $expected) {
        throw "SHA256SUMS in the release does not list $name."
    }

    # The archive and its checksum come from the same place, so this does not defend against whoever
    # controls that place. What it catches is a download that arrived damaged or cut short.
    $actual = (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash
    if ($actual -ne $expected) {
        throw "$name is damaged: its checksum does not match the release."
    }
}

function Get-AgentPackage([string] $WorkDir, [string] $Version, [string] $PackagePath) {
    if ($PackagePath) {
        if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
            throw "There is no package at $PackagePath."
        }
        return (Resolve-Path -LiteralPath $PackagePath).ProviderPath
    }

    # SERVERMONITOR_DOWNLOAD_BASE serves the packages from somewhere other than this repository's
    # releases: a fork, a mirror, or a test.
    if ($env:SERVERMONITOR_DOWNLOAD_BASE) {
        $base = $env:SERVERMONITOR_DOWNLOAD_BASE.TrimEnd('/')
    }
    elseif ($Version -eq 'latest') {
        $base = "https://github.com/$Repo/releases/latest/download"
    }
    else {
        $base = "https://github.com/$Repo/releases/download/$Version"
    }

    $archive = Join-Path $WorkDir $Asset
    $sums = Join-Path $WorkDir 'SHA256SUMS'

    Write-Step "Downloading $Asset ($Version)"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri "$base/$Asset" -OutFile $archive
        Invoke-WebRequest -UseBasicParsing -Uri "$base/SHA256SUMS" -OutFile $sums
    }
    catch {
        throw "Could not download the agent from ${base}: $($_.Exception.Message)"
    }

    Confirm-Checksum -File $archive -SumsFile $sums
    return $archive
}

function Protect-AgentFolder([string] $Path) {
    # The folder ends up holding the enrollment token and, once the agent registers, the key it was
    # issued. Program Files lets every local user read what is inside, so inheritance is cut and only
    # SYSTEM, which runs the agent, and Administrators keep access. Well-known SIDs rather than
    # account names, because the names are translated on localised Windows.
    $null = & icacls.exe $Path /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F'
    if ($LASTEXITCODE -ne 0) {
        throw "Could not restrict access to $Path (icacls exited with $LASTEXITCODE)."
    }
}

function Register-AgentTask([string] $InstallDir) {
    $exe = Join-Path $InstallDir $ExeName

    # Quoted, because the path runs through Program Files, which has a space in it.
    $action = New-ScheduledTaskAction -Execute ('"{0}"' -f $exe) -WorkingDirectory $InstallDir
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest

    # No time limit: by default a task is stopped after three days, which for an agent means it
    # quietly stops reporting on the fourth. Restarted when it fails, and allowed to run on battery,
    # since laptops are among the machines worth watching.
    $options = @{
        ExecutionTimeLimit         = [TimeSpan]::Zero
        RestartCount               = 999
        RestartInterval            = New-TimeSpan -Minutes 1
        AllowStartIfOnBatteries    = $true
        DontStopIfGoingOnBatteries = $true
        StartWhenAvailable         = $true
    }
    $taskSettings = New-ScheduledTaskSettingsSet @options

    $task = @{
        TaskName    = $TaskName
        Description = 'Reports this machine''s CPU, memory and disk to ServerMonitor.'
        Action      = $action
        Trigger     = $trigger
        Principal   = $principal
        Settings    = $taskSettings
        Force       = $true
    }
    $null = Register-ScheduledTask @task
}

function Wait-Agent([string] $InstallDir, [bool] $RegisteredBefore, [string] $ServerUrl) {
    $statePath = Join-Path $InstallDir 'agent-state.json'
    Write-Step 'Waiting for the agent to register'

    # Running is not the same as working: an agent retrying against an address that does not answer
    # runs too. So wait for the one thing that proves registration succeeded, the state file, or for
    # the task to stop, which is what the agent does with a token it cannot use.
    for ($waited = 0; $waited -lt 20; $waited++) {
        if ((Get-ScheduledTask -TaskName $TaskName).State -notin 'Running', 'Queued') {
            break
        }

        # An upgrade finds the file already there, so the new executable gets a few seconds to show
        # that it starts.
        if ((Test-Path -LiteralPath $statePath) -and (-not $RegisteredBefore -or $waited -ge 3)) {
            break
        }

        Start-Sleep -Seconds 1
    }

    if ((Get-ScheduledTask -TaskName $TaskName).State -notin 'Running', 'Queued') {
        $result = (Get-ScheduledTaskInfo -TaskName $TaskName).LastTaskResult
        if ($result -eq $ConfigurationErrorExitCode) {
            throw 'The agent stopped: the server refused the enrollment token, or there was none. Compare it with ENROLLMENT_TOKEN in the server''s .env and run this again with -Token.'
        }
        throw "The agent stopped with exit code $result. Its errors are in Event Viewer, under Windows Logs > Application."
    }

    if (-not (Test-Path -LiteralPath $statePath)) {
        Write-Warning "The agent is running but has not registered yet. It keeps retrying on its own; if the machine does not appear in the fleet, $ServerUrl is probably wrong or blocked by a firewall."
    }
}

function Install-Agent {
    param(
        [string] $InstallDir,
        [string] $Url,
        [string] $Token,
        [string] $Version,
        [string] $PackagePath
    )

    $configurationPath = Join-Path $InstallDir $ConfigurationName
    $configuration = Read-AgentConfiguration $configurationPath

    $serverUrl = $Url
    if (-not $serverUrl) {
        $serverUrl = Get-AgentSetting $configuration 'ServerUrl'
    }
    if (-not $serverUrl) {
        $serverUrl = Read-Host 'API address, such as http://192.168.1.20:7212'
    }

    $enrollmentToken = $Token
    if (-not $enrollmentToken) {
        $enrollmentToken = Get-AgentSetting $configuration 'EnrollmentToken'
    }
    if (-not $enrollmentToken) {
        # Read without echo, so the token does not stay behind on the screen.
        $secure = Read-Host 'Enrollment token (ENROLLMENT_TOKEN in the server''s .env)' -AsSecureString
        $enrollmentToken = (New-Object Net.NetworkCredential '', $secure).Password
    }

    if ($serverUrl -notmatch '^https?://\S+$') {
        throw "The address should look like http://192.168.1.20:7212, not '$serverUrl'."
    }
    if (-not $enrollmentToken) {
        throw 'An enrollment token is needed for the first registration.'
    }

    try {
        $null = Invoke-WebRequest -UseBasicParsing -Uri ($serverUrl.TrimEnd('/') + '/healthz') -TimeoutSec 5
    }
    catch {
        Write-Warning "$serverUrl does not answer from this machine. Installing anyway: the agent keeps retrying on its own, but check the address and any firewall in between."
    }

    $work = Join-Path ([IO.Path]::GetTempPath()) ('servermonitor-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $work

    try {
        $archive = Get-AgentPackage -WorkDir $work -Version $Version -PackagePath $PackagePath
        $contents = Join-Path $work 'agent'
        Expand-Archive -LiteralPath $archive -DestinationPath $contents
        if (-not (Test-Path -LiteralPath (Join-Path $contents $ExeName))) {
            throw "The package holds no $ExeName."
        }

        $registeredBefore = Test-Path -LiteralPath (Join-Path $InstallDir 'agent-state.json')

        Write-Step "Installing to $InstallDir"
        Stop-Agent -InstallDir $InstallDir
        $null = New-Item -ItemType Directory -Path $InstallDir -Force
        Protect-AgentFolder -Path $InstallDir
        Copy-Item -Path (Join-Path $contents '*') -Destination $InstallDir -Recurse -Force

        $configuration.Agent | Add-Member -NotePropertyName ServerUrl -NotePropertyValue $serverUrl -Force
        $configuration.Agent | Add-Member -NotePropertyName EnrollmentToken -NotePropertyValue $enrollmentToken -Force
        Write-AgentConfiguration $configuration $configurationPath

        Write-Step "Registering the scheduled task '$TaskName'"
        Register-AgentTask -InstallDir $InstallDir
        Start-ScheduledTask -TaskName $TaskName

        Wait-Agent -InstallDir $InstallDir -RegisteredBefore $registeredBefore -ServerUrl $serverUrl
    }
    finally {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host ''
    Write-Host 'The agent is running. The machine appears in the fleet with its first reading.'
    Write-Host ''
    Write-Host "  Get-ScheduledTask -TaskName '$TaskName'      is it running"
    Write-Host '  Event Viewer, Windows Logs > Application      its warnings and errors'
    Write-Host ''
    Write-Host 'To upgrade, or to change the address or the token, run the install command again.'
}

function Uninstall-Agent([string] $InstallDir) {
    Write-Step 'Stopping the agent'
    Stop-Agent -InstallDir $InstallDir

    if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    }

    # Only a folder that holds the agent is deleted, so a mistyped -InstallDir cannot take anything
    # else with it.
    if (Test-Path -LiteralPath (Join-Path $InstallDir $ExeName)) {
        Write-Step "Removing $InstallDir"
        Remove-Item -LiteralPath $InstallDir -Recurse -Force
    }

    $parent = Split-Path -Parent $InstallDir
    if ((Split-Path -Leaf $parent) -eq 'ServerMonitor' -and (Test-Path -LiteralPath $parent) -and -not (Get-ChildItem -LiteralPath $parent -Force)) {
        Remove-Item -LiteralPath $parent -Force
    }

    Write-Host ''
    Write-Host 'The agent is removed. The machine and its history stay in the fleet until you remove them from the machine''s page on the dashboard.'
}

if (-not (Test-Administrator)) {
    throw 'Run this in PowerShell opened as Administrator: right-click PowerShell and choose "Run as administrator".'
}

if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'The agent is built for 64-bit Windows only.'
}

# Windows PowerShell may offer only older TLS versions by default, and GitHub refuses those.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

if ($Uninstall) {
    Uninstall-Agent -InstallDir $InstallDir
}
else {
    Install-Agent -InstallDir $InstallDir -Url $Url -Token $Token -Version $Version -PackagePath $PackagePath
}
