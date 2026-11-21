<#
.SYNOPSIS
    Guarded, non-destructive insertion of one line of text into an EMPTY Visual Studio Copilot Chat
    draft through UI Automation. It never sends, never clears and never overwrites existing text.

.DESCRIPTION
    Wraps the AgentDebugToolkit `type --paste --verify` primitive (clipboard snapshot/restore,
    history/cloud exclusion, read-back) with the checks the orchestrator's Send script lacks:
      1. Target must be a devenv.exe started with the Exp root suffix (unless -AllowMainInstance).
      2. Chat input ("Ask Copilot") must be showing; no workspace/trust banner.
      3. SendButton must exist and be DISABLED. Enabled means the draft holds text; missing means the
         chat is busy (Send becomes Stop). Either way nothing is typed.
      4. The SendButton is re-checked after focus is acquired, immediately before typing.
      5. Paste with read-back. A verify mismatch (for example the person typed meanwhile) is reported
         as DraftChanged. The script does not clear: our text may still be inside the person's draft
         and cannot be separated from it, so the person must remove it.
      6. After success the SendButton must have become enabled. The Send button is never clicked.
    This is NOT atomic. On the clipboard-paste path text typed after the final check is kept and
    ours is added to it. If the input exposes a writable ValuePattern, the primitive uses SetValue,
    which replaces the whole draft, so text typed after the final check would be lost; the method
    used is reported. The previous foreground window is restored on exit when possible.

.PARAMETER Hwnd
    Target window handle, for example 0x311C0. Required; look it up each time (handles go stale).

.PARAMETER Text
    Single line, up to 4096 characters (up to 20000 with -Append -Paste).

.PARAMETER Append
    Experimental. Add Text to the END of an existing draft with synthetic keystrokes (no Ctrl+V). It is
    passive: it never switches windows or tabs, never moves focus, and sends no key chords, unless the
    person changes focus during the call (the CLI may then re-activate Exp and the text could land in
    another control). The user must already have Exp in the foreground with the chat input focused and the caret at the end of
    the draft; otherwise it refuses before sending any key. It reads the draft through the UIA
    TextPattern, never clears or selects, never sends, and verifies that the result is exactly the
    previous draft plus Text. Text is limited to letters, digits, space and _ . : - so no SendKeys
    special character can occur. Reports lengths only, never draft content.

.PARAMETER PlaceCaret
    With -Append only. Opt-in active mode: brings the target forward, focuses the chat input and sends
    one Ctrl+End, then requires the input to still be in the UI tree with unchanged text and the caret
    at the end before typing. Refuses otherwise. Needed when the caret cannot be placed by hand.

.PARAMETER Paste
    With -Append only. Deliver Text with the toolkit's paste-text verb (clipboard, snapshot restored)
    at the current caret instead of typing it, which is much faster for long text. Same guards and
    exact read-back as typing. Text up to 20000 characters on one line with no control characters and
    no double quote, not ending in a backslash. Otherwise the typed form is limited to 4096 safe characters.

.PARAMETER NoLeadingNewline
    With -Append -Paste only. By default, when the existing draft is not empty, the pasted text starts
    with a line break so the new prompt begins on its own line (a pasted line break never sends the
    message). This switch appends directly after the existing text instead. Typed -Append never adds a
    line break, because a typed Enter would send the message.

.PARAMETER AllowMainInstance
    Permit a non-Exp devenv.exe. Only after explicit user approval.

.PARAMETER InputName
    UIA Name of the chat input. Default "Ask Copilot".

.PARAMETER ExePath
    Path to agentdebug-ui.exe.

.OUTPUTS
    JSON: Status (Inserted | Refused | DraftChanged | Failed), Reason, MutationAttempted, Method.
    MutationAttempted is true only when text may have reached the input. The inserted text and the
    draft contents are never echoed.

.NOTES
    Modifies the target chat draft and the clipboard (restored by the primitive). Default and -PlaceCaret
    modes move focus; plain -Append does not.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Hwnd,
    [Parameter(Mandatory = $true)][string]$Text,
    [switch]$AllowMainInstance,
    [switch]$Append,
    [switch]$PlaceCaret,
    [switch]$Paste,
    [switch]$NoLeadingNewline,
    [string]$InputName = 'Ask Copilot',
    [string]$ExePath = "$env:USERPROFILE\.copilot\toolkits\AgentDebugToolkit\src\AgentDebugToolkit.UiAutomation.Cli\bin\Debug\net8.0-windows\agentdebug-ui.exe"
)

$ErrorActionPreference = 'Stop'
Remove-Item Env:DOTNET_ROOT -ErrorAction SilentlyContinue

function Write-Result([string]$Status, [string]$Reason, [bool]$Mutated = $false, [string]$Method = $null) {
    [pscustomobject]@{ Status = $Status; Reason = $Reason; MutationAttempted = $Mutated; Method = $Method } | ConvertTo-Json -Compress
}

if ($Hwnd -notmatch '^(0[xX])?[0-9A-Fa-f]{1,16}$') { return Write-Result 'Refused' 'Hwnd is not a hexadecimal window handle.' }

if (-not (Test-Path -LiteralPath $ExePath)) { return Write-Result 'Failed' 'agentdebug-ui.exe not found.' }
if ($Text.Length -eq 0 -or $Text.Length -gt 20000 -or ($Text.Length -gt 4096 -and -not ($Append -and $Paste)) -or $Text -match '[\x00-\x1F\x7F]') {
    return Write-Result 'Refused' 'Text must be 1 to 4096 characters (up to 20000 with -Append -Paste) on one line with no control characters.'
}
if (($Paste -or $PlaceCaret -or $NoLeadingNewline) -and -not $Append) { return Write-Result 'Refused' '-Paste, -PlaceCaret and -NoLeadingNewline are only valid together with -Append.' }
if ($NoLeadingNewline -and -not $Paste) { return Write-Result 'Refused' '-NoLeadingNewline is only valid together with -Paste.' }
if ($Append -and $Text.StartsWith('-')) { return Write-Result 'Refused' 'Append text must not start with a hyphen (command-line option parsing).' }

function Invoke-Ui([string[]]$CliArgs) {
    $raw = (& $ExePath @CliArgs 2>&1 | Out-String).Trim()
    try { $raw | ConvertFrom-Json } catch { [pscustomobject]@{ success = $false; error = 'unparseable-output' } }
}

function Find-UiNode($node, [string]$id) {
    if ($null -eq $node) { return $null }
    if ($node.AutomationId -eq $id) { return $node }
    foreach ($c in @($node.Children)) { $x = Find-UiNode $c $id; if ($x) { return $x } }
    $null
}

function Get-TreeRoot([string[]]$CliArgs) {
    $tree = Invoke-Ui $CliArgs
    if ($tree.PSObject.Properties.Name -contains 'result' -and $tree.result) { $tree = $tree.result }
    if ($tree.PSObject.Properties.Name -contains 'success' -and -not $tree.success) { return $null }
    $tree.root
}

function Get-SendButton {
    Find-UiNode (Get-TreeRoot @('inspect', '--hwnd', $Hwnd, '--nameContains', 'Send', '--controlType', 'Button', '--maxDepth', '25')) 'SendButton'
}

Add-Type -Namespace VcvNative -Name Win -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
'@

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

function Get-DraftState {
    try { Read-DraftState } catch { $null }
}

function Read-DraftState {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $InputName)),
        (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
    $found = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($found.Count -ne 1) { return $null }
    $tp = $null
    if (-not $found[0].TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern, [ref]$tp)) { return $null }
    $doc = $tp.DocumentRange
    $sel = $tp.GetSelection()
    $atEnd = $false
    if ($sel.Count -eq 1) {
        $start = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::Start
        $end = [System.Windows.Automation.Text.TextPatternRangeEndpoint]::End
        $atEnd = ($sel[0].CompareEndpoints($start, $sel[0], $end) -eq 0) -and ($sel[0].CompareEndpoints($start, $doc, $end) -eq 0)
    }
    [pscustomobject]@{ Text = $doc.GetText(-1); CaretAtEnd = $atEnd; HasFocus = [bool]$found[0].Current.HasKeyboardFocus }
}

function Invoke-Append {
    if ($Paste) {
        if ($Text.Contains('"')) { return Write-Result 'Refused' 'Paste text must not contain a double quote (command-line safety).' }
        if ($Text.EndsWith('\')) { return Write-Result 'Refused' 'Paste text must not end with a backslash (Windows PowerShell 5.1 would turn it into a double quote).' }
    } elseif ($Text -notmatch '^[A-Za-z0-9 _.:-]{1,4096}$') { return Write-Result 'Refused' 'Append text may only contain letters, digits, space and _ . : - (SendKeys safety).' }
    $method = if ($Paste) { 'clipboard-paste-focus' } else { 'synthetic-keyboard' }
    $before = Get-DraftState
    if ($null -eq $before) { return Write-Result 'Refused' 'Draft text cannot be read through TextPattern: nothing was typed.' }
    if ($PlaceCaret) {
        if (-not (Set-AgentForegroundWindow -Handle $handle)) { return Write-Result 'Refused' 'Could not bring the target forward: nothing was typed.' }
        $keys = Invoke-Ui @('send-keys', '--hwnd', $Hwnd, '--strategy', 'Name', '--value', $InputName, '--keys', '^{END}')
        $before2 = Get-DraftState
        if ($null -eq $before2 -or $before2.Text -cne $before.Text) { return Write-Result 'Refused' "Draft unreadable or changed after placing the caret (send-keys success=$($keys.success)): nothing was typed." }
        $before = $before2
    }
    if ([VcvNative.Win]::GetForegroundWindow() -ne $handle) { return Write-Result 'Refused' 'ExpNotForeground: bring Exp forward yourself; nothing was typed.' }
    if (-not $before.HasFocus) { return Write-Result 'Refused' 'ChatInputNotFocused: click into the chat input yourself; nothing was typed.' }
    if (-not $before.CaretAtEnd) { return Write-Result 'Refused' 'CaretNotAtEnd: place the caret at the end of the draft yourself; nothing was typed.' }
    $lead = $Paste -and -not $NoLeadingNewline -and $before.Text.Length -gt 0
    $payload = if ($lead) { "`n$Text" } else { $Text }
    # The editor may report a pasted line break as LF or CRLF, so accept either.
    $isExpected = {
        param($t)
        if ($lead) { ($t -ceq ($before.Text + "`n" + $Text)) -or ($t -ceq ($before.Text + "`r`n" + $Text)) -or ($t -ceq ($before.Text + "`r" + $Text)) }
        else { $t -ceq ($before.Text + $Text) }
    }
    $typed = if ($Paste) {
        Invoke-Ui @('paste-text', '--hwnd', $Hwnd, '--text', $payload)
    } else {
        Invoke-Ui @('send-keys', '--hwnd', $Hwnd, '--strategy', 'Name', '--value', $InputName, '--focusMode', 'none', '--keys', $Text)
    }
    if (-not $typed.success) {
        $check = Get-DraftState
        if ($null -ne $check -and $check.Text -ceq $before.Text) { return Write-Result 'Failed' "$($method) failed ($($typed.error)); the draft is unchanged." $false }
        return Write-Result 'Failed' "$($method) failed ($($typed.error)); the draft state is unknown." $true
    }
    $after = $null
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 250
        $after = Get-DraftState
        if ($null -ne $after -and (& $isExpected $after.Text)) { break }
    }
    if ($null -eq $after) { return Write-Result 'Failed' 'Could not re-read the draft; state unknown.' $true $method }
    if (& $isExpected $after.Text) {
        $nl = if ($lead) { ' on a new line' } else { '' }
        return Write-Result 'Inserted' "Appended $($Text.Length) characters$nl after $($before.Text.Length) existing; prior text preserved; not sent." $true $method
    }
    $kept = $after.Text.StartsWith($before.Text, [StringComparison]::Ordinal)
    Write-Result 'DraftChanged' "Unexpected result: before=$($before.Text.Length) after=$($after.Text.Length) priorTextPrefixKept=$kept. Nothing was cleared. If the draft lacks the text it may have reached another control." $true $method
}

$handle = [IntPtr][Convert]::ToInt64($Hwnd, 16)
[uint32]$ownerPid = 0
[void][VcvNative.Win]::GetWindowThreadProcessId($handle, [ref]$ownerPid)
if ($ownerPid -eq 0) { return Write-Result 'Refused' 'Window handle is not valid (stale?).' }
$owner = Get-CimInstance Win32_Process -Filter "ProcessId=$ownerPid"
if (-not $owner -or $owner.Name -ne 'devenv.exe') { return Write-Result 'Refused' 'Target window is not a devenv.exe window.' }
if (-not $AllowMainInstance -and $owner.CommandLine -notmatch '(?i)[/-]rootsuffix[\s:]+"?Exp"?(\s|$)') {
    return Write-Result 'Refused' 'Target is not an Exp instance; pass -AllowMainInstance only with explicit approval.'
}

$agentCursor = Join-Path $env:USERPROFILE '.copilot\skills\ui-interaction\scripts\AgentCursor.ps1'
if (-not (Test-Path -LiteralPath $agentCursor)) { return Write-Result 'Failed' 'ui-interaction AgentCursor.ps1 is not installed.' }
. $agentCursor
$previous = [VcvNative.Win]::GetForegroundWindow()
Enable-AgentCursor
try {
    # Cheap read-only guards run first; the target is brought forward only if the input is showing.
    $inputNode = Invoke-Ui @('find-first', '--hwnd', $Hwnd, '--strategy', 'Name', '--value', $InputName)
    $found = if ($inputNode.PSObject.Properties.Name -contains 'result' -and $inputNode.result) { $inputNode.result.found } else { $inputNode.found }
    if (-not $found) { return Write-Result 'Refused' 'ChatInputNotVisible: nothing was typed.' }

    $root = Get-TreeRoot @('inspect', '--hwnd', $Hwnd, '--maxDepth', '25')
    if ($null -eq $root) { return Write-Result 'Refused' 'Could not inspect the window for banners: nothing was typed.' }
    foreach ($banner in 'HarnessWorkspaceMismatchBanner', 'WorkspaceTrustWarningBanner') {
        if (Find-UiNode $root $banner) { return Write-Result 'Refused' "Banner $banner is showing: nothing was typed." }
    }

    $send = Get-SendButton
    if (-not $send) { return Write-Result 'Refused' 'SendButtonNotFound: the chat may be busy. Nothing was typed.' }
    if ($Append) { return Invoke-Append }
    if ($send.IsEnabled) { return Write-Result 'Refused' 'DraftNotEmpty: the chat input holds text. Nothing was typed.' }

    if (-not (Set-AgentForegroundWindow -Handle $handle)) { return Write-Result 'Refused' 'Could not bring the target forward: nothing was typed.' }

    # Focus changes take time; check again so the unguarded window is only the CLI start-up.
    $send = Get-SendButton
    if (-not $send) { return Write-Result 'Refused' 'SendButtonNotFound after focus: nothing was typed.' }
    if ($send.IsEnabled) { return Write-Result 'Refused' 'DraftNotEmpty after focus: nothing was typed.' }

    $typed = Invoke-Ui @('type', '--hwnd', $Hwnd, '--strategy', 'Name', '--value', $InputName, '--text', $Text, '--paste', '--verify')
    if (-not $typed.success) {
        switch -Regex ([string]$typed.error) {
            '^(clipboard-unavailable|window-not-responding|window-gone|element-not-found|not-foreground|window-not-found)$' {
                return Write-Result 'Failed' "Nothing was typed ($($typed.error))."
            }
            '^(verify-mismatch|paste-.*|.*instab.*)$' {
                return Write-Result 'DraftChanged' "$($typed.error): the draft is not what was pasted and was not cleared. Our text may still be inside it; the person must remove it." $true
            }
            default {
                return Write-Result 'Failed' "Unknown type failure ($($typed.error)); the draft state is unknown and was not cleared." $true
            }
        }
    }
    $method = if ($typed.PSObject.Properties.Name -contains 'result' -and $typed.result) { [string]$typed.result.method } else { [string]$typed.method }
    $after = Get-SendButton
    if (-not $after -or -not $after.IsEnabled) { return Write-Result 'Failed' 'Paste verified but the Send button did not become enabled; the draft was left as is.' $true $method }
    Write-Result 'Inserted' 'Text written and read back; not sent.' $true $method
}
finally {
    Restore-AgentCursor
    if ($previous -ne [IntPtr]::Zero -and $previous -ne $handle) { [void](Set-AgentForegroundWindow -Handle $previous) }
}
