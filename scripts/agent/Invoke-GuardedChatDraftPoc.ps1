<#
.SYNOPSIS
Probes atomic-insertion support for one explicitly identified Exp chat.
.DESCRIPTION
Native insertion currently fails closed: atomic editing is unproved. No draft is read or changed.
Never sends, activates, pastes, scrolls or retries.
Requires both bridge and draft process-local gates in Debug Exp. Pass identity/generation/revision
from a fresh Read-ChatBridgePoc snapshot. Retain the complete original request for reconciliation
if transport is uncertain; never automatically retry or replace its identity.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [ValidateRange(1, 2147483647)] [int]$TargetProcessId,
    [Parameter(Mandatory = $true)] [Guid]$RequestId,
    [Parameter(Mandatory = $true)] [Guid]$SessionId,
    [Parameter(Mandatory = $true)] [ValidateLength(1, 256)] [string]$ClientId,
    [Parameter(Mandatory = $true)] [ValidatePattern('^[0-9a-f]{32}$')] [string]$Generation,
    [Parameter(Mandatory = $true)] [ValidateRange(0, 9223372036854775806)] [long]$DraftRevision,
    [Parameter(Mandatory = $true)] [ValidateLength(1, 4096)] [string]$Transcript
)
$ErrorActionPreference = 'Stop'
function Write-PocFrame {
    param([IO.Stream]$Stream, [byte[]]$Frame, [Diagnostics.Stopwatch]$Clock,
        [int]$TimeoutMilliseconds = 15000)
    $remaining = $TimeoutMilliseconds - [int]$Clock.ElapsedMilliseconds
    if ($remaining -le 0) {
        $Stream.Dispose()
        throw 'Insertion outcome is unknown after timeout; retain the complete original request and do not retry.'
    }
    $write = $Stream.WriteAsync($Frame, 0, $Frame.Length)
    if (-not $write.Wait($remaining)) {
        $Stream.Dispose()
        throw 'Insertion outcome is unknown after timeout; retain the complete original request and do not retry.'
    }
    [void]$write.GetAwaiter().GetResult()
}
if ($RequestId -eq [Guid]::Empty -or $SessionId -eq [Guid]::Empty -or
    [string]::IsNullOrWhiteSpace($ClientId) -or [string]::IsNullOrWhiteSpace($Transcript)) {
    throw 'The guarded draft identity/text is invalid.'
}
$process = Get-Process -Id $TargetProcessId
if ($process.ProcessName -ne 'devenv' -or $process.MainWindowTitle -notlike '*Experimental Instance*') {
    throw 'TargetProcessId must identify the intended live Experimental Visual Studio instance.'
}
if (-not ('ChatBridgePoc.PipeIdentity' -as [type])) {
    Add-Type -Namespace ChatBridgePoc -Name PipeIdentity -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
public static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
'@
}
$payload = [ordered]@{
    Version = 1; RequestId = $RequestId.ToString('D'); SessionId = $SessionId.ToString('D')
    ClientId = $ClientId; Generation = $Generation; DraftRevision = $DraftRevision; Transcript = $Transcript
} | ConvertTo-Json -Compress
$body = [Text.Encoding]::UTF8.GetBytes($payload)
if ($body.Length -gt 32768) { throw 'The guarded draft payload exceeds its byte limit.' }
$pipe = New-Object IO.Pipes.NamedPipeClientStream('.', "VsCopilotChatVoice.Poc.$TargetProcessId",
    [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
$output = New-Object IO.MemoryStream
try {
    $pipe.Connect(5000)
    [uint32]$serverPid = 0
    if (-not [ChatBridgePoc.PipeIdentity]::GetNamedPipeServerProcessId($pipe.SafePipeHandle, [ref]$serverPid) -or
        $serverPid -ne $TargetProcessId) {
        throw 'The insertion pipe server PID does not match the requested Exp instance.'
    }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $header = [Text.Encoding]::ASCII.GetBytes("insert`n")
    $length = [BitConverter]::GetBytes([uint32]$body.Length)
    $frame = New-Object byte[] ($header.Length + $length.Length + $body.Length)
    [Buffer]::BlockCopy($header, 0, $frame, 0, $header.Length)
    [Buffer]::BlockCopy($length, 0, $frame, $header.Length, $length.Length)
    [Buffer]::BlockCopy($body, 0, $frame, $header.Length + $length.Length, $body.Length)
    Write-PocFrame -Stream $pipe -Frame $frame -Clock $clock
    $buffer = New-Object byte[] 4096
    while ($true) {
        $remaining = 15000 - [int]$clock.ElapsedMilliseconds
        if ($remaining -le 0) {
            $pipe.Dispose()
            throw 'Insertion outcome is unknown after timeout; retain the complete original request and do not retry.'
        }
        $read = $pipe.ReadAsync($buffer, 0, $buffer.Length)
        if (-not $read.Wait($remaining)) {
            $pipe.Dispose()
            throw 'Insertion outcome is unknown after timeout; retain the complete original request and do not retry.'
        }
        $count = $read.Result
        if ($count -eq 0) { break }
        if ($output.Length + $count -gt 262144) { throw 'Insertion response exceeds its size limit; retain RequestId.' }
        $output.Write($buffer, 0, $count)
    }
    if ($output.Length -eq 0) { throw 'No insertion acknowledgement; retain RequestId and inspect the Exp activity log.' }
    $result = [Text.Encoding]::UTF8.GetString($output.ToArray()) | ConvertFrom-Json
    if ([Guid]$result.RequestId -ne $RequestId -or $result.Status -notin @('Inserted', 'Rejected', 'InsertionUncertain')) {
        throw 'Insertion acknowledgement identity/status is invalid; retain RequestId.'
    }
    $result | ConvertTo-Json -Depth 4
    if ($result.Status -ne 'Inserted') {
        throw ('Guarded draft outcome: ' + $result.Status + ' / ' + $result.Reason + '. No retry was attempted.')
    }
}
finally {
    $pipe.Dispose()
    $output.Dispose()
}
