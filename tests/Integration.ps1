param([string]$SqlServer = '.\SQLEXPRESS', [int]$Port = 5089, [ValidateSet('Debug','Release')][string]$Configuration='Debug')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$root = (Get-Location).Path
$dotnetPath = Join-Path $root '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = (Get-Command dotnet).Source }
$relativeDll = "bin\$Configuration\net10.0\KieuViet.Assets.dll"
$dll = Join-Path $root $relativeDll
if (-not (Test-Path -LiteralPath $dll)) { throw 'Run dotnet build first.' }
$database = 'KVAssets_Test_' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$password = [guid]::NewGuid().ToString('N') + '!aA1'
$previousConnection = $env:ConnectionStrings__Assets
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousPassword = $env:KV_ADMIN_PASSWORD
$previousPersistence = $env:DataProtection__PersistToDatabase
$env:DataProtection__PersistToDatabase = 'true'
$env:ConnectionStrings__Assets = "Server=$SqlServer;Database=$database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:KV_ADMIN_PASSWORD = $password
$env:DOTNET_CLI_HOME = Join-Path $root '.tools\cli'
$base = "http://localhost:$Port"
$script:passed = 0
$process = $null
function Assert($condition, $message) { if (-not $condition) { throw "FAIL: $message" }; $script:passed++; Write-Host "PASS: $message" }
function Call([string]$path, [string]$method='GET', $data=$null, [int]$expect=200, [switch]$NoCsrf) {
    $params = @{ Uri=($base+'/api'+$path); Method=$method; WebSession=$script:session; UseBasicParsing=$true }
    if ($method -ne 'GET' -and -not $NoCsrf) { $params.Headers=@{'X-CSRF-TOKEN'=$script:csrf} }
    if ($null -ne $data) { $params.ContentType='application/json'; $params.Body=[Text.Encoding]::UTF8.GetBytes(($data|ConvertTo-Json -Depth 10 -Compress)) }
    try { $r=Invoke-WebRequest @params; $code=[int]$r.StatusCode; $body=$r.Content }
    catch { if ($null -eq $_.Exception.Response) { throw }; $code=[int]$_.Exception.Response.StatusCode; $reader=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream()); $body=$reader.ReadToEnd(); $reader.Dispose() }
    if ($code -ne $expect) { throw "Expected $expect, got $code at $method $path : $body" }
    $script:passed++; Write-Host "PASS: $method $path -> $expect"
    if ($body -and $body -is [string]) { try { return ($body|ConvertFrom-Json) } catch { return $body } }
    return $body
}
function Login([string]$username, [string]$secret) {
    $script:session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $script:csrf=(Call '/csrf').token
    $null=Call '/login' 'POST' @{username=$username;password=$secret}
    $script:csrf=(Call '/csrf').token
}
function ImportWorkbook([byte[]]$bytes, [int]$expected) {
    Add-Type -AssemblyName System.Net.Http
    $handler=New-Object Net.Http.HttpClientHandler
    $handler.CookieContainer=$script:session.Cookies
    $client=New-Object Net.Http.HttpClient($handler)
    try {
        $multipart=New-Object Net.Http.MultipartFormDataContent
        $part=New-Object Net.Http.ByteArrayContent(,$bytes)
        $multipart.Add($part,'file','assets.xlsx')
        $client.DefaultRequestHeaders.Add('X-CSRF-TOKEN',$script:csrf)
        $response=$client.PostAsync(($base+'/api/excel/import'),$multipart).GetAwaiter().GetResult()
        $text=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        Assert ([int]$response.StatusCode -eq $expected) "Excel import -> $expected ($text)"
    } finally { $client.Dispose() }
}
function ChangeWorkbook([byte[]]$bytes, [string]$code, [switch]$InvalidCost) {
    Add-Type -AssemblyName System.IO.Compression
    $memory=New-Object IO.MemoryStream
    $memory.Write($bytes,0,$bytes.Length);$memory.Position=0
    $zip=New-Object IO.Compression.ZipArchive($memory,[IO.Compression.ZipArchiveMode]::Update,$true)
    $entry=$zip.GetEntry('xl/worksheets/sheet1.xml')
    $reader=New-Object IO.StreamReader($entry.Open());[xml]$xml=$reader.ReadToEnd();$reader.Dispose()
    $ns=New-Object Xml.XmlNamespaceManager($xml.NameTable);$ns.AddNamespace('s','http://schemas.openxmlformats.org/spreadsheetml/2006/main')
    $cell=$xml.SelectSingleNode('//s:c[@r="A2"]',$ns)
    while($cell.HasChildNodes){$null=$cell.RemoveChild($cell.FirstChild)}
    $cell.SetAttribute('t','inlineStr');$inline=$xml.CreateElement('is',$ns.LookupNamespace('s'));$text=$xml.CreateElement('t',$ns.LookupNamespace('s'));$text.InnerText=$code;$null=$inline.AppendChild($text);$null=$cell.AppendChild($inline)
    if($InvalidCost){$cost=$xml.SelectSingleNode('//s:c[@r="I2"]/s:v',$ns);$cost.InnerText='-1'}
    $entry.Delete();$replacement=$zip.CreateEntry('xl/worksheets/sheet1.xml');$writer=New-Object IO.StreamWriter($replacement.Open());$writer.Write($xml.OuterXml);$writer.Dispose();$zip.Dispose()
    $result=$memory.ToArray();$memory.Dispose();return ,$result
}
try {
    & $dotnetPath $dll --init-db
    if ($LASTEXITCODE -ne 0) { throw 'Database initialization failed.' }
    Remove-Item Env:KV_ADMIN_PASSWORD
    $outLog=Join-Path $root '.tools\integration-server.log'; $errLog=Join-Path $root '.tools\integration-server-error.log'
    $process=Start-Process -FilePath $dotnetPath -ArgumentList @($relativeDll,'--urls',$base) -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
    $ready=$false
    for($i=0;$i -lt 30;$i++) { try { $null=Invoke-WebRequest ($base+'/api/csrf') -UseBasicParsing; $ready=$true; break } catch { Start-Sleep -Milliseconds 500 } }
    Assert $ready 'Server started'
    $script:session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $null=Call '/assets' 'GET' $null 401
    $script:csrf=(Call '/csrf').token
    $null=Call '/login' 'POST' @{username='admin';password=$password} 400 -NoCsrf
    Login 'admin' $password
    Assert ((Call '/me').role -eq 'Admin') 'Administrator authenticated'
    $usingStatus='"\u0110ang s\u1eed d\u1ee5ng"'|ConvertFrom-Json
    $unchecked='"Ch\u01b0a ki\u1ec3m k\u00ea"'|ConvertFrom-Json
    $matched='"Kh\u1edbp"'|ConvertFrom-Json
    $asset=@{code='TEST-001';name='Laptop integration';category='IT';department='Accounting';location='Room A';custodian='Test User';serial='SERIAL-1';purchaseDate='2026-01-01';cost=12500000;status=$usingStatus;notes='Test record';version=$null}
    $created=Call '/assets' 'POST' $asset 201
    Assert ($created.id -gt 0) 'Asset persisted in SQL Server'
    Assert ($created.updatedAt -match 'Z$') 'SQL timestamps include UTC timezone'
    $null=Call '/assets' 'POST' $asset 409
    $s=Call '/sessions' 'POST' @{name='Integration inventory';department='Accounting'}
    $asset.name='Laptop updated';$asset.location='Room B';$asset.version=$created.version
    $updated=Call ('/assets/'+$created.id) 'PUT' $asset
    $null=Call ('/assets/'+$created.id) 'PUT' $asset 409
    $items=@(Call ('/sessions/'+$s.id+'/items'))
    Assert ($items[0].expectedLocation -eq 'Room A' -and $items[0].name -eq 'Laptop integration') 'Session snapshot survives asset edits'
    $null=Call ('/sessions/'+$s.id+'/close') 'POST' $null 400
    $check=@{result=$matched;actualLocation='Room B';notes='Transferred';version=$items[0].version}
    $null=Call ('/sessions/'+$s.id+'/items/'+$items[0].id) 'PUT' $check
    $null=Call ('/sessions/'+$s.id+'/items/'+$items[0].id) 'PUT' $check 409
    $sessions=@(Call '/sessions')
    Assert ($sessions[0].checked -eq 1 -and $sessions[0].differences -eq 1) 'Location mismatch reported even with matched result'
    $null=Call ('/sessions/'+$s.id+'/close') 'POST'
    $null=Call ('/sessions/'+$s.id+'/items/'+$items[0].id) 'PUT' $check 409
    foreach ($path in @('/excel/template','/excel/assets',('/excel/sessions/'+$s.id))) {
        $r=Invoke-WebRequest ($base+'/api'+$path) -WebSession $script:session -UseBasicParsing
        Assert ($r.StatusCode -eq 200 -and $r.Headers['Content-Type'] -match 'spreadsheetml') "Excel download $path"
    }
    $export=Invoke-WebRequest ($base+'/api/excel/assets') -WebSession $script:session -UseBasicParsing
    $bytes=$export.RawContentStream.ToArray()
    ImportWorkbook $bytes 400
    $invalid=ChangeWorkbook $bytes 'IMPORTED-INVALID' -InvalidCost
    ImportWorkbook $invalid 400
    Assert (@(Call '/assets').Count -eq 1) 'Invalid workbook leaves database unchanged'
    $valid=ChangeWorkbook $bytes 'IMPORTED-001'
    ImportWorkbook $valid 200
    $allAssets=@(Call '/assets');Assert ($allAssets.Count -eq 2 -and ($allAssets|Where-Object code -eq 'IMPORTED-001').cost -eq 12500000) 'Excel values persisted correctly'
    $viewerPass=[guid]::NewGuid().ToString('N')+'!v1'
    $null=Call '/users' 'POST' @{username='viewer';displayName='Read Only';password=$viewerPass;role='Viewer'}
    $adminSession=$script:session;$adminCsrf=$script:csrf
    Login 'viewer' $viewerPass
    $null=Call '/assets'
    $null=Call '/assets' 'POST' $asset 403
    $null=Call '/users' 'GET' $null 403
    $null=Call '/audit' 'GET' $null 403
    $script:session=$adminSession;$script:csrf=$adminCsrf
    $logs=@(Call '/audit'); Assert ($logs.Count -ge 5) 'Audit entries recorded'
    $null=Call ('/assets/'+$created.id+'?version='+[uri]::EscapeDataString($updated.version)) 'DELETE' $null 204
    Assert (@(Call '/assets'|Where-Object code -eq 'TEST-001').Count -eq 0) 'Archived asset hidden from active catalog'
    Assert (@(Call ('/sessions/'+$s.id+'/items')).Count -eq 1) 'Archived asset inventory history retained'
    Stop-Process -Id $process.Id
    $process.WaitForExit()
    $process=Start-Process -FilePath $dotnetPath -ArgumentList @($relativeDll,'--urls',$base) -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput $outLog -RedirectStandardError $errLog
    $ready=$false
    for($i=0;$i -lt 30;$i++){try{$null=Invoke-WebRequest ($base+'/') -UseBasicParsing -TimeoutSec 2;$ready=$true;break}catch{Start-Sleep -Milliseconds 500}}
    Assert $ready 'Server restarted with database-backed keys'
    Assert ((Call '/me').username -eq 'admin') 'Authentication cookie survives process restart'
    $null=Call '/logout' 'POST' $null 204
    $null=Call '/assets' 'GET' $null 401
    Write-Host "SUCCESS: $script:passed checks. Isolated database retained: $database"
} finally {
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id }
    $env:ConnectionStrings__Assets=$previousConnection
    $env:ASPNETCORE_ENVIRONMENT=$previousEnvironment
    $env:KV_ADMIN_PASSWORD=$previousPassword
    $env:DataProtection__PersistToDatabase=$previousPersistence
}
