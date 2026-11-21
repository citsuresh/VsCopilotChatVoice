<#
Purpose: Verify a packaged VSIX version/identity and its installed binary hashes.
Parameters: VsixPath, InstalledDirectory, ExpectedVersion, BinaryNames and ManifestName.
Access: Read-only; does not install, extract, modify or delete files.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$VsixPath,
    [Parameter(Mandatory = $true)][string]$InstalledDirectory,
    [Parameter(Mandatory = $true)][string]$ExpectedVersion,
    [string[]]$BinaryNames = @('VsCopilotChatVoice.VisualStudio.dll', 'VsCopilotChatVoice.Reader.exe'),
    [string]$ManifestName = 'extension.vsixmanifest'
)

$ErrorActionPreference = 'Stop'
if ($BinaryNames.Count -eq 0) { throw 'At least one binary name is required.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $VsixPath).Path)
try {
    $manifestEntry = $zip.GetEntry($ManifestName)
    if ($null -eq $manifestEntry) { throw "Package manifest not found: $ManifestName" }
    $reader = New-Object IO.StreamReader($manifestEntry.Open())
    try { [xml]$packaged = $reader.ReadToEnd() } finally { $reader.Dispose() }
    [xml]$installed = Get-Content -LiteralPath (Join-Path $InstalledDirectory $ManifestName) -Raw
    $identity = $packaged.PackageManifest.Metadata.Identity
    $installedIdentity = $installed.PackageManifest.Metadata.Identity
    if ([string]::IsNullOrWhiteSpace($identity.Id) -or $identity.Id -ne $installedIdentity.Id) {
        throw 'The packaged and installed extension identities do not match.'
    }
    if ($identity.Version -ne $ExpectedVersion -or $installedIdentity.Version -ne $ExpectedVersion) {
        throw "Expected $ExpectedVersion; package=$($identity.Version), installed=$($installedIdentity.Version)."
    }
    $binaries = foreach ($name in $BinaryNames) {
        $entries = @($zip.Entries | Where-Object { $_.Name -eq $name })
        $files = @(Get-ChildItem -LiteralPath $InstalledDirectory -Filter $name -Recurse -File)
        if ($entries.Count -ne 1 -or $files.Count -ne 1) {
            throw "Expected one packaged and installed $name; package=$($entries.Count), installed=$($files.Count)."
        }
        $hash = [Security.Cryptography.SHA256]::Create()
        $stream = $entries[0].Open()
        try { $packagedHash = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $hash.Dispose() }
        $installedHash = (Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash
        if ($packagedHash -ne $installedHash) { throw "Installed binary differs from package: $name" }
        [pscustomobject]@{ Binary = $name; SHA256 = $packagedHash }
    }
    [pscustomobject]@{
        Version = $ExpectedVersion
        Identity = $identity.Id
        InstalledDirectory = (Resolve-Path -LiteralPath $InstalledDirectory).Path
        Binaries = @($binaries)
    }
}
finally { $zip.Dispose() }