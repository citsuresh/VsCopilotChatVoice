<#
.SYNOPSIS
Reads a bounded, chat-scoped accessibility snapshot for bridge capability research.
.DESCRIPTION
Read-only. Does not activate, type, scroll or submit. Names may contain chat content;
keep output local. Does not claim UI Automation exposes a stable conversation identity.
.PARAMETER Hwnd
Explicit live Visual Studio main-window handle, in hexadecimal.
.PARAMETER MaxNodes
Maximum node attempts across discovery and the chat snapshot.
.PARAMETER MaxDepth
Maximum chat-snapshot depth. Discovery has a separate depth cap of 30.
.PARAMETER MaxDurationSeconds
Cooperative time budget; an individual provider call cannot be interrupted.
.EXAMPLE
.\Read-ChatBridgeSurface.ps1 -Hwnd 0x1234 -MaxDepth 5
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^0x[0-9a-fA-F]+$')]
    [string]$Hwnd,
    [ValidateRange(1, 10000)] [int]$MaxNodes = 3000,
    [ValidateRange(1, 30)] [int]$MaxDepth = 5,
    [ValidateRange(1, 120)] [int]$MaxDurationSeconds = 20
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr][Convert]::ToInt64($Hwnd.Substring(2), 16))
if (-not $root) { throw 'The supplied window is unavailable.' }
$process = Get-Process -Id $root.Current.ProcessId -ErrorAction Stop
if ($process.ProcessName -ne 'devenv') { throw 'The supplied window is not Visual Studio.' }
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
$clock = [Diagnostics.Stopwatch]::StartNew()
$script:visited = 0

function Assert-Budget {
    $script:visited++
    if ($script:visited -gt $MaxNodes -or $clock.Elapsed.TotalSeconds -ge $MaxDurationSeconds) {
        throw 'Observation budget exceeded; this snapshot is incomplete and must not establish absence.'
    }
}

$queue = New-Object 'System.Collections.Generic.Queue[object]'
$queue.Enqueue(@{ Element = $root; Depth = 0 })
$panes = @()
while ($queue.Count -gt 0) {
    Assert-Budget
    $entry = $queue.Dequeue()
    $node = $entry.Element
    $current = $node.Current
    if ($current.ClassName -eq 'GenericPane' -and $current.Name -eq 'Chat' -and -not $current.IsOffscreen) {
        $panes += $node
        continue
    }
    if ($entry.Depth -ge 30) { throw 'Discovery depth exceeded; the pane inventory is incomplete.' }
    $child = $walker.GetFirstChild($node)
    while ($null -ne $child) {
        Assert-Budget
        $queue.Enqueue(@{ Element = $child; Depth = $entry.Depth + 1 })
        $child = $walker.GetNextSibling($child)
    }
}
if ($panes.Count -ne 1) { throw "Expected exactly one visible Chat pane; found $($panes.Count)." }

$queue.Enqueue(@{ Element = $panes[0]; Depth = 0 })
$rows = @()
$depthLimited = $false
while ($queue.Count -gt 0) {
    Assert-Budget
    $entry = $queue.Dequeue()
    $node = $entry.Element
    $current = $node.Current
    $name = $current.Name
    if ($name.Length -gt 300) { $name = $name.Substring(0, 300) + ' [truncated]' }
    $rows += [pscustomobject]@{
        Depth = $entry.Depth
        Class = $current.ClassName
        Id = $current.AutomationId
        Name = $name
        ItemStatus = $current.ItemStatus
        Help = $current.HelpText
        RuntimeId = ($node.GetRuntimeId() -join '.')
        Enabled = $current.IsEnabled
        Offscreen = $current.IsOffscreen
        Patterns = @($node.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName })
    }
    $child = $walker.GetFirstChild($node)
    if ($entry.Depth -ge $MaxDepth) {
        if ($null -ne $child) { $depthLimited = $true }
        continue
    }
    while ($null -ne $child) {
        Assert-Budget
        $queue.Enqueue(@{ Element = $child; Depth = $entry.Depth + 1 })
        $child = $walker.GetNextSibling($child)
    }
}
[pscustomobject]@{
    Pid = $process.Id
    ProcessStartUtc = $process.StartTime.ToUniversalTime().ToString('o')
    Hwnd = $Hwnd
    DepthLimited = $depthLimited
    Attempts = $script:visited
    ElapsedMs = $clock.ElapsedMilliseconds
    Nodes = $rows
} | ConvertTo-Json -Depth 6