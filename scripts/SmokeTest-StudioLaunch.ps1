param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,
    [string[]]$Arguments = @(),
    [Parameter(Mandatory = $true)]
    [int]$Port,
    [int]$StartupTimeoutSeconds = 30,
    [switch]$AllowLauncherExit
)

$ErrorActionPreference = 'Stop'

$resolvedPath = (Resolve-Path -LiteralPath $FilePath).Path
$workingDirectory = Split-Path -Parent $resolvedPath
$uri = "http://127.0.0.1:$Port/"

Write-Host "Launching Studio smoke target: $resolvedPath $($Arguments -join ' ')"
$process = Start-Process -FilePath $resolvedPath -ArgumentList $Arguments -WorkingDirectory $workingDirectory -PassThru

try {
    $deadline = (Get-Date).AddSeconds($StartupTimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500

        try {
            $response = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -ge 200) {
                Write-Host "Studio responded successfully at $uri"
                return
            }
        }
        catch {
        }

        if (-not $AllowLauncherExit -and $process.HasExited) {
            throw "Studio launcher exited before Studio responded at $uri."
        }
    }

    throw "Studio did not respond at $uri within $StartupTimeoutSeconds seconds."
}
finally {
    if ($process -and -not $process.HasExited) {
        $null = $process.CloseMainWindow()
        Start-Sleep -Seconds 2
    }

    $portOwners = @(Get-NetTCPConnection -LocalAddress 127.0.0.1 -LocalPort $Port -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
    foreach ($ownerPid in $portOwners) {
        if ($ownerPid -and (Get-Process -Id $ownerPid -ErrorAction SilentlyContinue)) {
            Stop-Process -Id $ownerPid -Force
        }
    }

    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
}
