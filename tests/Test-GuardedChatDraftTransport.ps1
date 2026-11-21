[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot '..\scripts\agent\Invoke-GuardedChatDraftPoc.ps1'
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($helper, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'The guarded draft client does not parse.' }
$function = $ast.Find({ param($node)
    $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Write-PocFrame'
}, $false)
if ($null -eq $function) { throw 'The deadline-bound writer is missing.' }
Invoke-Expression $function.Extent.Text

$frame = New-Object byte[] 32768
$memory = New-Object IO.MemoryStream
try {
    Write-PocFrame -Stream $memory -Frame $frame -Clock ([Diagnostics.Stopwatch]::StartNew())
    if ($memory.Length -ne $frame.Length) { throw 'The completed writer did not transmit the full frame.' }
}
finally { $memory.Dispose() }

$memory = New-Object IO.MemoryStream
try {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Milliseconds 30
    try {
        Write-PocFrame -Stream $memory -Frame $frame -Clock $clock -TimeoutMilliseconds 1
        throw 'An expired deadline allowed transmission.'
    }
    catch {
        if ($_.Exception.Message -notlike 'Insertion outcome is unknown after timeout;*') { throw }
    }
    if ($memory.CanWrite -or $memory.ToArray().Length -ne 0) {
        throw 'Expired transmission did not dispose the stream before writing.'
    }
}
finally { $memory.Dispose() }

foreach ($mode in @('control', 'deadline', 'complete')) {
    $name = 'VsCopilotChatVoice.ClientDeadlineTest.' + [Guid]::NewGuid().ToString('N')
    $server = New-Object IO.Pipes.NamedPipeServerStream($name, [IO.Pipes.PipeDirection]::InOut, 1,
        [IO.Pipes.PipeTransmissionMode]::Byte, [IO.Pipes.PipeOptions]::Asynchronous, 4096, 4096)
    $client = New-Object IO.Pipes.NamedPipeClientStream('.', $name, [IO.Pipes.PipeDirection]::InOut,
        [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $connected = $server.WaitForConnectionAsync()
        $client.Connect(1000)
        if (-not $connected.Wait(1000)) { throw 'The isolated pipe fixture did not connect.' }
        if ($mode -eq 'complete') {
            $smallFrame = [Text.Encoding]::ASCII.GetBytes("insert`n")
            $received = New-Object byte[] $smallFrame.Length
            $read = $server.ReadAsync($received, 0, $received.Length)
            $unexpected = @(Write-PocFrame -Stream $client -Frame $smallFrame -Clock ([Diagnostics.Stopwatch]::StartNew()))
            if ($unexpected.Count -ne 0 -or -not $read.Wait(1000) -or $read.Result -ne $smallFrame.Length -or
                [Text.Encoding]::ASCII.GetString($received) -ne "insert`n") {
                throw 'Successful named-pipe writing emitted non-JSON pipeline output or changed its frame.'
            }
        }
        elseif ($mode -eq 'control') {
            $pending = $client.WriteAsync($frame, 0, $frame.Length)
            if ($pending.Wait(100)) { throw 'The non-reading fixture did not reproduce write backpressure.' }
        }
        else {
            $handle = $client.SafePipeHandle
            $clock = [Diagnostics.Stopwatch]::StartNew()
            try {
                Write-PocFrame -Stream $client -Frame $frame -Clock $clock -TimeoutMilliseconds 350
                throw 'A non-reading peer completed transmission unexpectedly.'
            }
            catch {
                if ($_.Exception.Message -notlike 'Insertion outcome is unknown after timeout;*') { throw }
            }
            if ($clock.ElapsedMilliseconds -lt 300 -or $clock.ElapsedMilliseconds -gt 5000 -or -not $handle.IsClosed) {
                throw ('Pending write: elapsed=' + $clock.ElapsedMilliseconds + ' ms, handleClosed=' + $handle.IsClosed + '.')
            }
            Write-Host ('Non-reading peer: disposed within ' + $clock.ElapsedMilliseconds + ' ms (350 ms deadline).')
        }
    }
    finally { $client.Dispose(); $server.Dispose() }
}
Write-Host 'Guarded draft transport: parsing, complete frame, clean pipeline, pre-write expiry, real backpressure and write deadline passed.'
