<#
.SYNOPSIS
    Read-only observation of the messages in one Visual Studio Copilot Chat through UI Automation.
    It sends no key, click or paste, changes no focus, window or tab, and never touches the draft.

.DESCRIPTION
    Runs ONE AgentDebugToolkit `inspect` of the target window and reads the chat transcript list
    (class ChatSessionList, items of class ChatMessageItem):
      * items alternate header, body: a short item (name up to 60 characters, at most 2 children) in the
        header position is an author header, and "GitHub Copilot" marks the next item as an assistant
        reply, any other header marks it as a user message. A body is never classified by its length;
        an item in the header position that does not look like a header is reported Unknown;
      * an item named "The response used ..." after a reply is the completion footer;
      * the full message text is the item's UIA Name.
    A reply is reported Complete only when its footer is present AND the SendButton exists
    (a missing SendButton means the chat is busy: Send becomes Stop).
    Guards: target must be a devenv.exe started with the Exp root suffix (unless -AllowMainInstance),
    exactly one ChatSessionList must be present (otherwise AmbiguousChat), and the inspected tree
    must not be truncated.
    The streaming state itself (footer absent while a reply is arriving) has not been observed live.

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
    JSON: Status (Observed | Refused | Failed), Reason, ChatBusy, MessageCount, Messages (Index, Role,
    Length, Sha256, Complete) and LastReply (Index, Length, Sha256, Complete, Text only with -IncludeText).

.NOTES
    Read-only. Reply identity is positional (Index) plus Length and Sha256; the UI exposes no
    per-message AutomationId.
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
$sendButton = $false
function Walk($n) {
    if ($null -eq $n) { return }
    if ($n.ClassName -eq 'ChatSessionList') { [void]$lists.Add($n) }
    if ($n.AutomationId -eq 'SendButton') { $script:sendButton = $true }
    foreach ($c in @($n.Children)) { Walk $c }
}
Walk $tree.root

if ($lists.Count -eq 0) { return Write-Result 'Refused' 'NoChatTranscript: no ChatSessionList is in the UI tree (chat hidden or closed).' }
if ($lists.Count -gt 1) { return Write-Result 'Refused' "AmbiguousChat: $($lists.Count) transcripts are in the UI tree." }

$sha = [System.Security.Cryptography.SHA256]::Create()
function Get-Sha([string]$s) { ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($s))) -replace '-', '').ToLowerInvariant() }

$messages = New-Object System.Collections.ArrayList
# Items alternate header, body (and an optional footer after an assistant body). A body is never
# classified by its length, so a very short reply cannot be mistaken for a header.
$expectBody = $false
$bodyRole = 'Unknown'
$lastBodyRole = $null
$index = 0
foreach ($item in @($lists[0].Children)) {
    if ($item.ClassName -ne 'ChatMessageItem') { continue }
    $name = [string]$item.Name
    $kids = @($item.Children).Count
    if ($expectBody) {
        $role = $bodyRole
        $expectBody = $false
        $lastBodyRole = $role
    }
    elseif ($lastBodyRole -eq 'Assistant' -and $name -match '^The response used ') {
        $role = 'Footer'
        $lastBodyRole = $null
    }
    elseif ($kids -le 2 -and $name.Length -le 60) {
        $role = 'Header'
        $bodyRole = if ($name -eq 'GitHub Copilot') { 'Assistant' } else { 'User' }
        $expectBody = $true
        $lastBodyRole = $null
    }
    else {
        $role = 'Unknown'
        $lastBodyRole = $null
    }
    [void]$messages.Add([pscustomobject]@{ Index = $index; Role = $role; Length = $name.Length; Sha256 = $(if ($role -in 'Assistant', 'User') { Get-Sha $name } else { $null }); Name = $name; Complete = $false })
    $index++
}

for ($i = 0; $i -lt $messages.Count; $i++) {
    if ($messages[$i].Role -eq 'Assistant') {
        $hasFooter = ($i + 1 -lt $messages.Count) -and $messages[$i + 1].Role -eq 'Footer'
        $messages[$i].Complete = $hasFooter -and $sendButton
    }
}

$assistants = @($messages | Where-Object { $_.Role -eq 'Assistant' })
$last = $null
if ($assistants.Count -gt 0) {
    $a = $assistants[-1]
    $last = [ordered]@{ Index = $a.Index; Length = $a.Length; Sha256 = $a.Sha256; Complete = $a.Complete }
    if ($IncludeText) { $last['Text'] = $a.Name }
}

Write-Result 'Observed' 'Read-only observation; nothing was sent, clicked or focused.' ([ordered]@{
    ChatBusy     = (-not $sendButton)
    MessageCount = $messages.Count
    Messages     = @($messages | ForEach-Object { [pscustomobject]@{ Index = $_.Index; Role = $_.Role; Length = $_.Length; Sha256 = $_.Sha256; Complete = $_.Complete } })
    LastReply    = $last
})
