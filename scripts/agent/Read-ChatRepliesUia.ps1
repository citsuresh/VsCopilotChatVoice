<#
.SYNOPSIS
    Read-only observation of the messages in one Visual Studio Copilot Chat through UI Automation.
    It sends no key, click or paste, changes no focus, window or tab, and never touches the draft.

.DESCRIPTION
    Runs ONE AgentDebugToolkit `inspect` of the target window and reads the chat transcript list
    (class ChatSessionList, items of class ChatMessageItem):
      * items are classified by structural markers, never by length or position alone: a footer contains
        PART_LikeButton, a message body contains PART_FlowScrollViewer, a tool call or task group contains
        toolCallExpander or HeaderSite, and any other short item (name up to 60 characters, at most 2
        children) is a header. "GitHub Copilot" starts an assistant turn; any other header (author name,
        "Restore Checkpoint") starts a user turn, except that inside an open assistant turn only
        "GitHub Copilot" or "Restore Checkpoint" counts (file chips like "README.md" stay Unknown);
      * an assistant turn can hold several bodies and tool items before its footer;
      * the full message text is the item's UIA Name.
    The last reply is the last assistant turn (all its bodies, joined with a line feed). It is Complete
    only when its footer is present AND the chat is idle: a visible Send button and no visible Cancel
    button (while a reply streams, Send is offscreen and Cancel is visible and enabled).
    The UI tree keeps only recent items, so MessageCount and earlier turns can change between calls.
    Guards: target must be a devenv.exe started with the Exp root suffix (unless -AllowMainInstance),
    exactly one ChatSessionList must be present (otherwise AmbiguousChat), and the inspected tree
    must not be truncated.

.PARAMETER Hwnd
    Target window handle, for example 0x4C1296. Required; look it up each time (handles go stale).

.PARAMETER IncludeText
    Also return the text of the last assistant reply. Off by default: the default output is lengths
    and SHA-256 hashes only, never message content.

.PARAMETER AllowMainInstance
    Permit a non-Exp devenv.exe. Only after explicit user approval.

.PARAMETER MaxNodes
    Node cap for the inspect call (default 8000). A truncated tree is refused.

.PARAMETER ExePath
    Path to agentdebug-ui.exe.

.OUTPUTS
    JSON: Status (Observed | Refused | Failed), Reason, ChatBusy, SendButton, CancelButton, MessageCount,
    Messages (Index, Turn, Role, Length, Sha256) and LastReply (Turn, FirstIndex, BodyCount, ToolItems,
    Length, Sha256, LaterTurns, Complete, Text only with -IncludeText). LaterTurns counts turns after
    the reply (a new user prompt makes it non-zero and the reply not Complete).

.NOTES
    Read-only. Reply identity is the position of its first body plus Length and Sha256; the UI exposes
    no per-message AutomationId.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Hwnd,
    [switch]$IncludeText,
    [switch]$AllowMainInstance,
    [int]$MaxNodes = 8000,
    [string]$ExePath = "$env:USERPROFILE\.copilot\toolkits\AgentDebugToolkit\src\AgentDebugToolkit.UiAutomation.Cli\bin\Debug\net8.0-windows\agentdebug-ui.exe"
)

$ErrorActionPreference = 'Stop'
Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue

function Write-Result([string]$Status, [string]$Reason, $Extra = $null) {
    $o = [ordered]@{ Status = $Status; Reason = $Reason }
    if ($Extra) { foreach ($k in $Extra.Keys) { $o[$k] = $Extra[$k] } }
    [pscustomobject]$o | ConvertTo-Json -Compress -Depth 6
}

if ($Hwnd -notmatch '^(0[xX])?[0-9A-Fa-f]{1,16}$') { return Write-Result 'Refused' 'Hwnd is not a hexadecimal window handle.' }
if ($MaxNodes -lt 100 -or $MaxNodes -gt 50000) { return Write-Result 'Refused' 'MaxNodes must be between 100 and 50000.' }
if (-not (Test-Path -LiteralPath $ExePath)) { return Write-Result 'Failed' 'agentdebug-ui.exe not found.' }

Add-Type -Namespace VcvNative -Name WinRead -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
'@

$handle = [IntPtr][Convert]::ToInt64($Hwnd, 16)
[uint32]$ownerPid = 0
[void][VcvNative.WinRead]::GetWindowThreadProcessId($handle, [ref]$ownerPid)
if ($ownerPid -eq 0) { return Write-Result 'Refused' 'Window handle is not valid (stale?).' }
$owner = Get-CimInstance Win32_Process -Filter "ProcessId=$ownerPid"
if (-not $owner -or $owner.Name -ne 'devenv.exe') { return Write-Result 'Refused' 'Target window is not a devenv.exe window.' }
if (-not $AllowMainInstance -and $owner.CommandLine -notmatch '(?i)[/-]rootsuffix[\s:]+"?Exp"?(\s|$)') {
    return Write-Result 'Refused' 'Target is not an Exp instance; pass -AllowMainInstance only with explicit approval.'
}

$raw = (& $ExePath inspect --hwnd $Hwnd --maxDepth 60 --maxNodes $MaxNodes 2>&1 | Out-String).Trim()
try { $tree = $raw | ConvertFrom-Json } catch { return Write-Result 'Failed' 'Inspect output could not be parsed.' }
if ($tree.PSObject.Properties.Name -contains 'result' -and $tree.result) { $tree = $tree.result }
if ($tree.PSObject.Properties.Name -contains 'success' -and -not $tree.success) { return Write-Result 'Failed' 'Inspect failed.' }
if ($tree.truncated) { return Write-Result 'Refused' 'The inspected tree was truncated; raise -MaxNodes.' }

$lists = New-Object System.Collections.ArrayList
$script:sendNode = $null
$script:cancelNode = $null
function Walk($n) {
    if ($null -eq $n) { return }
    if ($n.ClassName -eq 'ChatSessionList') { [void]$lists.Add($n) }
    if ($n.AutomationId -eq 'SendButton') { $script:sendNode = $n }
    if ($n.AutomationId -eq 'CancelButton') { $script:cancelNode = $n }
    foreach ($c in @($n.Children)) { Walk $c }
}
Walk $tree.root
# Idle needs a visible Send button and no visible Cancel button.
$sendButton = ($null -ne $script:sendNode) -and (-not $script:sendNode.IsOffscreen) -and (($null -eq $script:cancelNode) -or $script:cancelNode.IsOffscreen)

if ($lists.Count -eq 0) { return Write-Result 'Refused' 'NoChatTranscript: no ChatSessionList is in the UI tree (chat hidden or closed).' }
if ($lists.Count -gt 1) { return Write-Result 'Refused' "AmbiguousChat: $($lists.Count) transcripts are in the UI tree." }

$sha = [System.Security.Cryptography.SHA256]::Create()
function Get-Sha([string]$s) { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($s))) -replace '-', '').ToLowerInvariant() }

function Get-DescendantIds($n, $set) {
    if ($null -eq $n) { return }
    if ($n.AutomationId) { [void]$set.Add([string]$n.AutomationId) }
    foreach ($c in @($n.Children)) { Get-DescendantIds $c $set }
}

# Classification uses structural markers, never length or position alone:
#   Footer    contains PART_LikeButton
#   Body      contains PART_FlowScrollViewer (a user or assistant message; role comes from the turn)
#   Tool      contains toolCallExpander or HeaderSite (a tool call or task group)
#   Header    short item without those markers; "GitHub Copilot" starts an assistant turn, any other
#             header (author name, "Restore Checkpoint") starts a user turn. Inside an open assistant
#             turn (no footer yet) only "GitHub Copilot" or "Restore Checkpoint" counts as a header,
#             so file chips such as "README.md" inside a reply are not mistaken for one.
$messages = New-Object System.Collections.ArrayList
$userHeaders = New-Object 'System.Collections.Generic.HashSet[string]'
$turnRole = $null
$turn = 0
$index = 0
foreach ($item in @($lists[0].Children)) {
    if ($item.ClassName -ne 'ChatMessageItem') { continue }
    $name = [string]$item.Name
    $ids = New-Object 'System.Collections.Generic.HashSet[string]'
    Get-DescendantIds $item $ids
    if ($ids.Contains('PART_LikeButton')) { $role = 'Footer'; $thisTurn = $turn; $turnRole = $null }
    elseif ($ids.Contains('PART_FlowScrollViewer')) { $role = if ($turnRole) { $turnRole } else { 'Unknown' }; $thisTurn = $turn }
    elseif ($ids.Contains('toolCallExpander') -or $ids.Contains('HeaderSite')) { $role = 'Tool'; $thisTurn = $turn }
    elseif (@($item.Children).Count -le 2 -and $name.Length -le 60 -and ($turnRole -ne 'Assistant' -or $name -eq 'GitHub Copilot' -or $name -eq 'Restore Checkpoint' -or $userHeaders.Contains($name))) {
        $role = 'Header'
        # An author name seen outside an open assistant turn is remembered, so it still ends an unfinished reply.
        if ($turnRole -ne 'Assistant' -and $name -ne 'GitHub Copilot' -and $name -ne 'Restore Checkpoint') { [void]$userHeaders.Add($name) }
        $turn++
        $thisTurn = $turn
        $turnRole = if ($name -eq 'GitHub Copilot') { 'Assistant' } else { 'User' }
    }
    else { $role = 'Unknown'; $thisTurn = $turn }
    [void]$messages.Add([pscustomobject]@{ Index = $index; Turn = $thisTurn; Role = $role; Length = $name.Length; Sha256 = $(if ($role -in 'Assistant', 'User') { Get-Sha $name } else { $null }); Name = $name })
    $index++
}

# The last assistant turn starts at the last "GitHub Copilot" header and runs to its footer (or the end
# while streaming). It can have no body yet (only tool items, or just started).
$lastTurn = $null
foreach ($m in $messages) { if ($m.Role -eq 'Header' -and $m.Name -eq 'GitHub Copilot') { $lastTurn = $m.Turn } }
$last = $null
if ($null -ne $lastTurn) {
    $parts = @($messages | Where-Object { $_.Turn -eq $lastTurn })
    $bodies = @($parts | Where-Object { $_.Role -eq 'Assistant' })
    $hasFooter = [bool]($parts | Where-Object { $_.Role -eq 'Footer' })
    $joined = ($bodies | ForEach-Object { $_.Name }) -join "`n"
    $last = [ordered]@{
        Turn      = $lastTurn
        FirstIndex = $(if ($bodies.Count -gt 0) { $bodies[0].Index } else { $null })
        BodyCount = $bodies.Count
        ToolItems = @($parts | Where-Object { $_.Role -eq 'Tool' }).Count
        Length    = $joined.Length
        Sha256    = Get-Sha $joined
        LaterTurns = ($turn - $lastTurn)
        Complete  = ($hasFooter -and $sendButton -and $turn -eq $lastTurn)
    }
    if ($IncludeText) { $last['Text'] = $joined }
}

Write-Result 'Observed' 'Read-only observation; nothing was sent, clicked or focused.' ([ordered]@{
    ChatBusy     = (-not $sendButton)
    SendButton   = $(if ($script:sendNode) { [ordered]@{ Offscreen = [bool]$script:sendNode.IsOffscreen; Enabled = [bool]$script:sendNode.IsEnabled } } else { $null })
    CancelButton = $(if ($script:cancelNode) { [ordered]@{ Offscreen = [bool]$script:cancelNode.IsOffscreen; Enabled = [bool]$script:cancelNode.IsEnabled } } else { $null })
    MessageCount = $messages.Count
    Messages     = @($messages | ForEach-Object { [pscustomobject]@{ Index = $_.Index; Turn = $_.Turn; Role = $_.Role; Length = $_.Length; Sha256 = $_.Sha256 } })
    LastReply    = $last
})
