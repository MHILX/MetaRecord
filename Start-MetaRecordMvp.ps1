param(
    [int]$ApiPort = 5050,
    [int]$EditorPort = 5173,
    [switch]$SeparateWindows
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$apiProject = Join-Path $repoRoot 'src/MetaRecord.Web/MetaRecord.Web.csproj'
$editorRoot = Join-Path $repoRoot 'src/MetaRecord.Editor'

function Get-FreePort {
    param(
        [int]$StartPort
    )

    for ($port = $StartPort; $port -lt $StartPort + 20; $port++) {
        $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $listener) {
            return $port
        }
    }

    throw "No free port was found near $StartPort."
}

if (-not (Test-Path -LiteralPath $apiProject)) {
    throw "API project not found: $apiProject"
}

if (-not (Test-Path -LiteralPath $editorRoot)) {
    throw "Editor directory not found: $editorRoot"
}

if (-not (Get-Command npx -ErrorAction SilentlyContinue)) {
    throw 'npx is required to launch the editor.'
}

if (-not (Get-Command powershell -ErrorAction SilentlyContinue) -and -not (Get-Command pwsh -ErrorAction SilentlyContinue)) {
    throw 'A PowerShell host is required to launch the startup commands.'
}

if (Get-Command pwsh -ErrorAction SilentlyContinue) {
    $shellExecutable = 'pwsh'
}
else {
    $shellExecutable = 'powershell'
}

if ($ApiPort -eq $EditorPort) {
    throw 'The API port and editor port must be different.'
}

if ($ApiPort -eq 0 -or $EditorPort -eq 0) {
    throw 'Port values must be greater than zero.'
}

if ($EditorPort -lt 1024) {
    throw 'The editor port must be 1024 or higher.'
}

if ($ApiPort -lt 1024) {
    throw 'The API port must be 1024 or higher.'
}

$apiPort = Get-FreePort -StartPort $ApiPort
$editorPort = Get-FreePort -StartPort $EditorPort
$apiUrl = "http://127.0.0.1:$apiPort"
$editorUrl = "http://127.0.0.1:$editorPort"

if ($apiPort -ne $ApiPort) {
    Write-Host "Port $ApiPort is busy, using $apiPort for the API."
}

if ($editorPort -ne $EditorPort) {
    Write-Host "Port $EditorPort is busy, using $editorPort for the editor."
}

function Start-MetaRecordProcess {
    param(
        [string]$Name,
        [string]$WorkingDirectory,
        [string]$Command
    )

    if ($SeparateWindows) {
        Start-Process -FilePath $shellExecutable -ArgumentList @(
            '-NoExit'
            '-Command'
            $Command
        ) -WorkingDirectory $WorkingDirectory | Out-Null

        return $null
    }

    $logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'MetaRecord'
    if (-not (Test-Path -LiteralPath $logDirectory)) {
        $null = New-Item -ItemType Directory -Path $logDirectory -Force
    }

    $stdoutPath = Join-Path $logDirectory ("{0}-{1}.stdout.log" -f $Name, [guid]::NewGuid().ToString('N'))
    $stderrPath = Join-Path $logDirectory ("{0}-{1}.stderr.log" -f $Name, [guid]::NewGuid().ToString('N'))
    $process = Start-Process -FilePath $shellExecutable -ArgumentList @(
        '-NoProfile'
        '-Command'
        $Command
    ) -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru

    return [pscustomobject]@{
        Name           = $Name
        Process        = $process
        StdOutPath     = $stdoutPath
        StdErrPath     = $stderrPath
        StdOutPosition = 0L
        StdErrPosition = 0L
    }
}

function Write-AppendedLogLines {
    param(
        [string]$Path,
        [string]$Prefix,
        [ref]$Position
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)

    try {
        $null = $stream.Seek($Position.Value, [System.IO.SeekOrigin]::Begin)
        $reader = [System.IO.StreamReader]::new($stream)

        try {
            while (-not $reader.EndOfStream) {
                $line = $reader.ReadLine()
                if ($null -ne $line) {
                    Write-Host ("[{0}] {1}" -f $Prefix, $line)
                }
            }

            $Position.Value = $stream.Position
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Write-MetaRecordLogs {
    param(
        [object[]]$TrackedProcesses
    )

    foreach ($trackedProcess in $TrackedProcesses) {
        Write-AppendedLogLines -Path $trackedProcess.StdOutPath -Prefix $trackedProcess.Name -Position ([ref]$trackedProcess.StdOutPosition)
        Write-AppendedLogLines -Path $trackedProcess.StdErrPath -Prefix ("{0}:err" -f $trackedProcess.Name) -Position ([ref]$trackedProcess.StdErrPosition)
    }
}

function Stop-MetaRecordProcesses {
    param(
        [object[]]$TrackedProcesses
    )

    foreach ($trackedProcess in $TrackedProcesses) {
        if (-not $trackedProcess.Process.HasExited) {
            Stop-Process -Id $trackedProcess.Process.Id -Force
        }
    }
}

function Remove-MetaRecordLogs {
    param(
        [object[]]$TrackedProcesses
    )

    foreach ($trackedProcess in $TrackedProcesses) {
        foreach ($logPath in @($trackedProcess.StdOutPath, $trackedProcess.StdErrPath)) {
            if (Test-Path -LiteralPath $logPath) {
                Remove-Item -LiteralPath $logPath -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

$apiCommand = "dotnet run --project `"$apiProject`" --urls $apiUrl"

Write-Host "Starting MetaRecord API on $apiUrl..."
$trackedProcesses = @()
$apiProcess = Start-MetaRecordProcess -Name 'api' -WorkingDirectory $repoRoot -Command $apiCommand
if ($null -ne $apiProcess) {
    $trackedProcesses += $apiProcess
}

$editorCommand = @"
`$env:VITE_API_PROXY_TARGET = '$apiUrl'
Set-Location -LiteralPath "$editorRoot"
npx vite --host 127.0.0.1 --port $editorPort --strictPort --open
"@

Write-Host "Starting MetaRecord Editor on $editorUrl..."
Start-MetaRecordProcess -Name 'editor' -WorkingDirectory $editorRoot -Command $editorCommand | ForEach-Object {
    if ($null -ne $_) {
        $trackedProcesses += $_
    }
}

if ($SeparateWindows) {
    Write-Host "Editor will open at $editorUrl"
}
else {
    Write-Host "Editor will open at $editorUrl in the current terminal session"
    Write-Host 'Press Ctrl+C in this terminal to stop both processes.'

    try {
        while ($true) {
            Write-MetaRecordLogs -TrackedProcesses $trackedProcesses

            $exitedProcesses = @($trackedProcesses | Where-Object { $_.Process.HasExited })
            if ($exitedProcesses.Count -gt 0) {
                Write-MetaRecordLogs -TrackedProcesses $trackedProcesses

                $failedProcesses = @($exitedProcesses | Where-Object { $_.Process.ExitCode -ne 0 })
                if ($failedProcesses.Count -gt 0) {
                    $failureMessage = ($failedProcesses | ForEach-Object {
                        "{0} exited with code {1}" -f $_.Name, $_.Process.ExitCode
                    }) -join '; '

                    throw $failureMessage
                }

                $stoppedNames = ($exitedProcesses | ForEach-Object { $_.Name }) -join ', '
                Write-Host "Stopping because $stoppedNames exited."
                break
            }

            Start-Sleep -Milliseconds 200
        }
    }
    finally {
        Stop-MetaRecordProcesses -TrackedProcesses $trackedProcesses
        Write-MetaRecordLogs -TrackedProcesses $trackedProcesses
        Remove-MetaRecordLogs -TrackedProcesses $trackedProcesses
    }
}