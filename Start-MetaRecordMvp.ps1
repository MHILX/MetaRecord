param(
    [int]$ApiPort = 5050,
    [int]$EditorPort = 5173
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
    throw 'A PowerShell host is required to launch the helper windows.'
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

Write-Host "Starting MetaRecord API on $apiUrl..."
Start-Process -FilePath $shellExecutable -ArgumentList @(
    '-NoExit'
    '-Command'
    "dotnet run --project `"$apiProject`" --urls $apiUrl"
) -WorkingDirectory $repoRoot | Out-Null

$editorCommand = @"
`$env:VITE_API_PROXY_TARGET = '$apiUrl'
Set-Location -LiteralPath "$editorRoot"
npx vite --host 127.0.0.1 --port $editorPort --strictPort --open
"@

Write-Host "Starting MetaRecord Editor on $editorUrl..."
Start-Process -FilePath $shellExecutable -ArgumentList @(
    '-NoExit'
    '-Command'
    $editorCommand
) -WorkingDirectory $editorRoot | Out-Null

Write-Host "Editor will open at $editorUrl"