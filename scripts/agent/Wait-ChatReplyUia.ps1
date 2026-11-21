<#
.SYNOPSIS
    Read-only wait for a NEW completed Copilot Chat reply in an Exp window (UIA, experimental).

.DESCRIPTION
    Polls Read-ChatRepliesUia.ps1 (one inspect call per poll; it sends no key, click or paste and
    changes no focus or draft) until the last assistant reply is Complete (footer present, no later
    turn, chat idle), its SHA-256 differs from -AfterSha256, and the same hash was seen on
    -SettlePolls consecutive polls (default 2; settled, so a flicker between streaming and idle is not
    delivered).
    Pass the Sha256 of the reply seen before sending a prompt as -AfterSha256; that is the
    duplicate-delivery guard: the same reply is never reported as new. A new reply with identical text
    is accepted only after this wait saw the chat busy or a newer turn, so start waiting before or right
    after sending. Without -AfterSha256 an
    already completed reply is delivered once it settles.
    A Refused or Failed observation stops the wait at once (no retry loop over a refusal).

.PARAMETER Hwnd
    Target window handle, for example 0x4C1296. Required; look it up each time.

.PARAMETER AfterSha256
    SHA-256 of the previously delivered reply. A reply with this hash is not a new reply.

.PARAMETER TimeoutSeconds
    Maximum wait (default 120, range 5-1800). A poll that starts before the deadline runs to completion,
    so the wait can overrun by one observer call (a few seconds); a hung inspect call is not interrupted.

.PARAMETER PollSeconds
    Pause between polls (default 2, range 1-60, at most half of TimeoutSeconds).

.PARAMETER SettlePolls
    Consecutive polls that must show the same new complete hash (default 2, range 2-10). A live run saw one
    transient poll report a short partial body as Complete while the chat looked idle (list container reuse);
    raise this when a stalled stream must not be delivered early.

.PARAMETER IncludeText
    Also return the reply text (kept local; off by default).

.PARAMETER AllowMainInstance
    Passed through to the observer. Only after explicit user approval.

.PARAMETER ObserverPath
    Path to Read-ChatRepliesUia.ps1 (default: next to this script).

.OUTPUTS
    JSON: Status (Completed | TimedOut | Refused | Failed), Reason, Polls, and Observation (the last
    observer output, including LastReply).

.NOTES
    Read-only. Verified only for idle/duplicate/timeout behavior unless a streaming reply was observed live.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Hwnd,
    [string]$AfterSha256 = '',
    [int]$TimeoutSeconds = 120,
    [int]$PollSeconds = 2,
    [int]$SettlePolls = 2,
    [switch]$IncludeText,
    [switch]$AllowMainInstance,
    [string]$ObserverPath = (Join-Path $PSScriptRoot 'Read-ChatRepliesUia.ps1')
)

$ErrorActionPreference = 'Stop'

function Write-Out([string]$Status, [string]$Reason, [int]$Polls, $Observation) {
    [pscustomobject][ordered]@{ Status = $Status; Reason = $Reason; Polls = $Polls; Observation = $Observation } |
        ConvertTo-Json -Compress -Depth 8
}

if ($AfterSha256 -and $AfterSha256 -notmatch '^[0-9A-Fa-f]{64}$') { return Write-Out 'Refused' 'AfterSha256 must be 64 hexadecimal characters.' 0 $null }
if ($TimeoutSeconds -lt 5 -or $TimeoutSeconds -gt 1800) { return Write-Out 'Refused' 'TimeoutSeconds must be between 5 and 1800.' 0 $null }
if ($PollSeconds -lt 1 -or $PollSeconds -gt 60) { return Write-Out 'Refused' 'PollSeconds must be between 1 and 60.' 0 $null }
if ($SettlePolls -lt 2 -or $SettlePolls -gt 10) { return Write-Out 'Refused' 'SettlePolls must be between 2 and 10.' 0 $null }
if ($PollSeconds * 2 -gt $TimeoutSeconds) { return Write-Out 'Refused' 'PollSeconds must be at most half of TimeoutSeconds.' 0 $null }
if (-not (Test-Path -LiteralPath $ObserverPath)) { return Write-Out 'Failed' 'Observer script not found.' 0 $null }

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$polls = 0
$candidate = $null
$stable = 0
$sawActivity = $false
$last = $null
do {
    $polls++
    $args2 = @{ Hwnd = $Hwnd }
    if ($IncludeText) { $args2['IncludeText'] = $true }
    if ($AllowMainInstance) { $args2['AllowMainInstance'] = $true }
    try { $json = & $ObserverPath @args2 }
    catch { return Write-Out 'Failed' 'Observer threw an exception.' $polls $null }
    try { $last = $json | ConvertFrom-Json } catch { return Write-Out 'Failed' 'Observer output could not be parsed.' $polls $null }
    if ($null -eq $last) { return Write-Out 'Failed' 'Observer returned no output.' $polls $null }
    if ($last.Status -ne 'Observed') { return Write-Out $last.Status $last.Reason $polls $last }

    $r = $last.LastReply
    # A busy chat or a newer turn means a prompt was sent during the wait, so identical reply text is new.
    if ($last.ChatBusy -or ($r -and $r.LaterTurns -gt 0)) { $sawActivity = $true }
    $isNew = $r -and $r.Complete -and ((-not $AfterSha256) -or $sawActivity -or ($r.Sha256 -ne $AfterSha256.ToLowerInvariant()))
    if ($isNew) {
        if ($candidate -eq $r.Sha256) { $stable++ } else { $candidate = $r.Sha256; $stable = 1 }
        if ($stable -ge $SettlePolls) { return Write-Out 'Completed' 'New reply is complete and settled.' $polls $last }
    }
    else { $candidate = $null; $stable = 0 }
    $remaining = ($deadline - (Get-Date)).TotalSeconds
    if ($remaining -le 0) { break }
    Start-Sleep -Milliseconds ([int](1000 * [Math]::Min($PollSeconds, $remaining)))
} while ($true)

Write-Out 'TimedOut' 'No new completed, settled reply before the timeout.' $polls $last
