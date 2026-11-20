<#
.SYNOPSIS
Reads the explicitly enabled Exp-only diagnostic pipe from one live VS process.
.DESCRIPTION
Read-only. Verifies the connected server PID, bounds response size/time, and returns
chat control metadata/identity candidates. Does not insert, send, activate or scroll.
.PARAMETER TargetProcessId
Explicit PID of the Experimental Visual Studio instance started with the PoC enabled.
.EXAMPLE
.\Read-ChatBridgePoc.ps1 -TargetProcessId 12345
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$TargetProcessId
)
$ErrorActionPreference = 'Stop'
$process = Get-Process -Id $TargetProcessId -ErrorAction Stop
if ($process.ProcessName -ne 'devenv' -or $process.MainWindowTitle -notlike '*Experimental Instance*') {
    throw 'TargetProcessId must identify the intended live Experimental Visual Studio instance.'
}
if (-not ('ChatBridgePoc.PipeIdentity' -as [type])) {
    Add-Type -Namespace ChatBridgePoc -Name PipeIdentity -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
public static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
'@
}
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', "VsCopilotChatVoice.Poc.$TargetProcessId",
    [System.IO.Pipes.PipeDirection]::InOut, [System.IO.Pipes.PipeOptions]::Asynchronous)
$output = New-Object System.IO.MemoryStream
try {
    $pipe.Connect(5000)
    [uint32]$serverProcessId = 0
    if (-not [ChatBridgePoc.PipeIdentity]::GetNamedPipeServerProcessId($pipe.SafePipeHandle, [ref]$serverProcessId)) {
        throw 'Could not verify the diagnostic pipe server process.'
    }
    if ($serverProcessId -ne $TargetProcessId) { throw 'Diagnostic pipe server PID does not match the requested VS instance.' }
    $command = [Text.Encoding]::ASCII.GetBytes("inspect`n")
    $pipe.Write($command, 0, $command.Length)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $buffer = New-Object byte[] 4096
    while ($true) {
        $remaining = 15000 - [int]$clock.ElapsedMilliseconds
        if ($remaining -le 0) { throw 'Diagnostic response timed out.' }
        $read = $pipe.ReadAsync($buffer, 0, $buffer.Length)
        if (-not $read.Wait($remaining)) { throw 'Diagnostic response timed out.' }
        $count = $read.Result
        if ($count -eq 0) { break }
        if ($output.Length + $count -gt 262144) { throw 'Diagnostic response exceeds its size limit.' }
        $output.Write($buffer, 0, $count)
    }
    if ($output.Length -eq 0) { throw 'Diagnostic server returned no snapshot; inspect its activity log.' }
    $json = [Text.Encoding]::UTF8.GetString($output.ToArray())
    $json | ConvertFrom-Json | ConvertTo-Json -Depth 8
}
finally {
    $pipe.Dispose()
    $output.Dispose()
}