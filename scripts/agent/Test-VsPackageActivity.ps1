<#
Purpose: Verify exact-source entry/error counts in a Visual Studio activity log without emitting its contents.
Parameters: LogPath, Source, MinimumEntries and ExpectedErrors.
Access: Read-only; does not change files, VS, focus or settings.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [ValidateNotNullOrEmpty()][string]$Source = 'VsCopilotChatVoice',
    [ValidateRange(0, 2147483647)][int]$MinimumEntries = 1,
    [ValidateRange(0, 2147483647)][int]$ExpectedErrors = 0
)

$ErrorActionPreference = 'Stop'
$settings = New-Object Xml.XmlReaderSettings
$settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
$settings.XmlResolver = $null
$reader = [Xml.XmlReader]::Create((Resolve-Path -LiteralPath $LogPath).Path, $settings)
$document = New-Object Xml.XmlDocument
$document.XmlResolver = $null
try { $document.Load($reader) } finally { $reader.Dispose() }
if ($document.DocumentElement.Name -ne 'activity') { throw 'Expected a Visual Studio activity log.' }
$entries = @($document.SelectNodes('/activity/entry') | Where-Object { $_.source -ceq $Source })
$errors = @($entries | Where-Object { $_.type -ieq 'Error' })
if ($entries.Count -lt $MinimumEntries) {
    throw "Expected at least $MinimumEntries entries for $Source; found $($entries.Count)."
}
if ($errors.Count -ne $ExpectedErrors) {
    throw "Expected $ExpectedErrors errors for $Source; found $($errors.Count)."
}
[pscustomobject]@{ Source = $Source; Entries = $entries.Count; Errors = $errors.Count }