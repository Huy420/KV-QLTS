param([int]$Port=5090, [int]$DebugPort=9234)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$root=(Get-Location).Path
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:DOTNET_CLI_HOME=Join-Path $root '.tools\cli'
$dotnetPath=Join-Path $root '.tools\dotnet\dotnet.exe'
if(-not(Test-Path -LiteralPath $dotnetPath)){$dotnetPath=(Get-Command dotnet).Source}
$chromePath='C:\Program Files\Google\Chrome\Application\chrome.exe'
$server=$null;$browser=$null;$socket=$null;$script:messageId=0;$script:checks=0
function Assert($condition,$message){if(-not $condition){throw "FAIL: $message"};$script:checks++;Write-Host "PASS: $message"}
function Cdp([string]$method,$parameters=@{}){
    $script:messageId++;$id=$script:messageId
    $json=@{id=$id;method=$method;params=$parameters}|ConvertTo-Json -Depth 20 -Compress
    $bytes=[Text.Encoding]::UTF8.GetBytes($json)
    $segment=New-Object 'ArraySegment[byte]' (,$bytes)
    $socket.SendAsync($segment,[Net.WebSockets.WebSocketMessageType]::Text,$true,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
    while($true){
        $stream=New-Object IO.MemoryStream
        do{$buffer=New-Object byte[] 65536;$chunk=New-Object 'ArraySegment[byte]' (,$buffer);$read=$socket.ReceiveAsync($chunk,[Threading.CancellationToken]::None).GetAwaiter().GetResult();$stream.Write($buffer,0,$read.Count)}while(-not $read.EndOfMessage)
        $result=[Text.Encoding]::UTF8.GetString($stream.ToArray())|ConvertFrom-Json;$stream.Dispose()
        if($result.id -eq $id){if($result.error){throw ($result.error|ConvertTo-Json)};return $result.result}
    }
}
function Js([string]$expression){$r=Cdp 'Runtime.evaluate' @{expression=$expression;awaitPromise=$true;returnByValue=$true};if($r.exceptionDetails){throw ($r.exceptionDetails|ConvertTo-Json -Depth 10)};return $r.result.value}
try{
    $server=Start-Process -FilePath $dotnetPath -ArgumentList @('bin\Debug\net10.0\KieuViet.Assets.dll','--urls',"http://localhost:$Port") -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardOutput '.tools\browser-server.log' -RedirectStandardError '.tools\browser-server-error.log'
    $ready=$false;for($i=0;$i -lt 30;$i++){try{$null=Invoke-WebRequest "http://localhost:$Port/" -UseBasicParsing;$ready=$true;break}catch{Start-Sleep -Milliseconds 500}}
    Assert $ready 'Static app served'
    $profile=Join-Path $root ('.tools\chrome-qa-'+[guid]::NewGuid().ToString('N').Substring(0,8))
    $browser=Start-Process -FilePath $chromePath -ArgumentList @('--headless=new','--disable-gpu','--no-first-run','--no-proxy-server','--remote-debugging-address=127.0.0.1',"--remote-debugging-port=$DebugPort",('"--user-data-dir='+$profile+'"'),'about:blank') -WindowStyle Hidden -PassThru -RedirectStandardOutput '.tools\chrome-output.log' -RedirectStandardError '.tools\chrome-error.log'
    $tabs=$null;for($i=0;$i -lt 10;$i++){try{$tabs=Invoke-RestMethod "http://127.0.0.1:$DebugPort/json" -TimeoutSec 2;break}catch{Start-Sleep -Milliseconds 300}}
    if(-not $tabs){throw 'Chrome debugging endpoint unavailable.'}
    $tab=$tabs|Where-Object type -eq 'page'|Select-Object -First 1
    $socket=New-Object Net.WebSockets.ClientWebSocket
    $null=$socket.ConnectAsync([uri]$tab.webSocketDebuggerUrl,[Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $null=Cdp 'Page.enable';$null=Cdp 'Emulation.setDeviceMetricsOverride' @{width=1440;height=1000;deviceScaleFactor=1;mobile=$false}
    $null=Cdp 'Page.navigate' @{url="http://localhost:$Port/?demo=1"}
    $loaded=$false;for($i=0;$i -lt 30;$i++){if(Js "document.querySelector('#app') && !document.querySelector('#app').hidden && document.querySelectorAll('.stat').length===4"){$loaded=$true;break};Start-Sleep -Milliseconds 300}
    Assert $loaded 'Dashboard executes JavaScript and displays four statistics'
    Assert (Js "document.querySelectorAll('tbody tr').length===5") 'Recent assets table rendered'
    Assert (Js "!document.querySelector('#demo-banner').hidden") 'Demo data clearly labeled'
    $image=Cdp 'Page.captureScreenshot' @{format='png';captureBeyondViewport=$false}
    [IO.File]::WriteAllBytes((Join-Path $root '.tools\dashboard.png'),[Convert]::FromBase64String($image.data))
    $null=Js "navigate('assets')"
    Assert (Js "document.querySelectorAll('tbody tr').length===10") 'Asset pagination displays first ten rows'
    $null=Js "state.search='Dell';renderAssets()"
    Assert (Js "document.querySelectorAll('tbody tr').length===3") 'Asset search filters records'
    $null=Js "assetModal(); document.querySelector('#modal').open"
    Assert (Js "document.querySelectorAll('#modal input').length===9") 'Asset form opens with editable fields'
    $null=Js "document.querySelector('#modal').close();state.selectedSession=1;navigate('inventory')"
    Assert (Js "document.querySelectorAll('tbody tr').length===12") 'Inventory detail renders snapshot items'
    $null=Js "checkModal(9)"
    Assert (Js "document.querySelector('#modal select').options.length===5") 'Inventory result form exposes expected options'
    $null=Js "document.querySelector('#modal').close();navigate('reports')"
    Assert (Js "document.querySelectorAll('tbody tr').length===1") 'Difference report shows changed location'
    $null=Js "navigate('departments')"
    Assert (Js "document.querySelectorAll('.department-card').length===6") 'Departments aggregated from records'
    $null=Js "navigate('users')"
    Assert (Js "document.querySelectorAll('tbody tr').length===1") 'Users screen renders'
    $null=Cdp 'Emulation.setDeviceMetricsOverride' @{width=390;height=844;deviceScaleFactor=1;mobile=$true}
    $null=Js "navigate('dashboard')"
    Assert (Js "document.documentElement.scrollWidth<=390") 'Mobile dashboard fits viewport'
    $null=Js "document.querySelector('#menu-toggle').click()"
    Assert (Js "document.querySelector('.sidebar').classList.contains('open')") 'Mobile navigation opens'
    $null=Js "document.querySelector('.sidebar').classList.remove('open')"
    $null=Js "new Promise(resolve=>setTimeout(resolve,300))"
    Assert (Js "document.querySelector('.sidebar').getBoundingClientRect().right<=1") 'Mobile navigation closes fully'
    $image=Cdp 'Page.captureScreenshot' @{format='png';captureBeyondViewport=$false}
    [IO.File]::WriteAllBytes((Join-Path $root '.tools\mobile.png'),[Convert]::FromBase64String($image.data))
    Write-Host "SUCCESS: $script:checks browser checks. Screenshots: .tools/dashboard.png and .tools/mobile.png"
}finally{
    if($socket){$socket.Dispose()}
    if($browser -and -not $browser.HasExited){Stop-Process -Id $browser.Id}
    if($server -and -not $server.HasExited){Stop-Process -Id $server.Id}
}
