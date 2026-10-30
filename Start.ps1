[CmdletBinding()]
param(
    [switch]$Initialize,
    [switch]$InitializeOnly,
    [switch]$Demo,
    [switch]$NoBuild,
    [ValidateRange(1024,65535)][int]$Port = 5080
)
$ErrorActionPreference = 'Stop'
if ($Demo -and ($Initialize -or $InitializeOnly)) { throw 'Demo does not require database initialization.' }
$previousLocation = Get-Location
$envNames = @('DOTNET_CLI_HOME','NUGET_PACKAGES','ASPNETCORE_ENVIRONMENT','ConnectionStrings__Assets','KV_ADMIN_PASSWORD')
$previousEnv = @{}
foreach ($name in $envNames) { $previousEnv[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    Set-Location -LiteralPath $PSScriptRoot
    $dotnetPath = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetPath)) {
        $command = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $command) { throw 'Install .NET SDK 10 first: https://dotnet.microsoft.com/download/dotnet/10.0' }
        $dotnetPath = $command.Source
    }
    $env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
    $env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    if (-not $env:ConnectionStrings__Assets) {
        $env:ConnectionStrings__Assets = 'Server=.\SQLEXPRESS;Database=KieuVietAssets;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
    }
    $project = Join-Path $PSScriptRoot 'KieuViet.Assets.csproj'
    if (-not $NoBuild) {
        & $dotnetPath build $project --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Build failed. Fix the errors above before starting.' }
    }
    $dll = Join-Path $PSScriptRoot 'bin\Debug\net10.0\KieuViet.Assets.dll'
    if (-not (Test-Path -LiteralPath $dll)) { throw 'Build output missing. Run again without -NoBuild.' }
    if ($Initialize -or $InitializeOnly) {
        $secret = Read-Host 'Dat mat khau admin (toi thieu 12 ky tu)' -AsSecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
        try {
            $env:KV_ADMIN_PASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
            if ($env:KV_ADMIN_PASSWORD.Length -lt 12 -or $env:KV_ADMIN_PASSWORD.Length -gt 256) { throw 'Password must contain 12 to 256 characters.' }
            & $dotnetPath $dll --init-db
            if ($LASTEXITCODE -ne 0) { throw 'Database initialization failed. Check SQL Server and connection permissions.' }
        } finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
            Remove-Item Env:KV_ADMIN_PASSWORD -ErrorAction SilentlyContinue
        }
        if ($InitializeOnly) { Write-Host 'Database initialized. Login: admin (password you just entered).'; return }
    }
    Remove-Item Env:KV_ADMIN_PASSWORD -ErrorAction SilentlyContinue
    $url = "http://localhost:$Port"
    $listener = New-Object Net.Sockets.TcpClient
    try {
        $pending = $listener.ConnectAsync('localhost', $Port)
        if ($pending.Wait(1000) -and $listener.Connected) {
            throw "Port $Port is already in use. Open $url if the app is already running, or run Start.ps1 -Port $($Port + 1). Database initialization, if completed above, is preserved."
        }
    } catch [System.AggregateException] {
        # Connection refused means no listener was detected; Kestrel checks the final bind.
    } finally {
        $listener.Dispose()
    }
    Write-Host ''
    if ($Demo) { Write-Host "BAN MINH HOA: $url/?demo=1 (du lieu mau tren trinh duyet)" }
    else { Write-Host "UNG DUNG: $url"; Write-Host 'Lan dau: dung Start.ps1 -Initialize de tao database va tai khoan admin.' }
    Write-Host 'Nhan Ctrl+C de dung. Chi truy cap tren may nay (localhost).'
    & $dotnetPath $dll --urls $url
    if ($LASTEXITCODE -ne 0) { throw "Application stopped with exit code $LASTEXITCODE. Check the port and server log above." }
} finally {
    foreach ($name in $envNames) { [Environment]::SetEnvironmentVariable($name, $previousEnv[$name], 'Process') }
    Set-Location -LiteralPath $previousLocation.Path
}
