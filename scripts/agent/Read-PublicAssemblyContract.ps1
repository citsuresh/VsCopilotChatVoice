<#
.SYNOPSIS
Reads public .NET Framework assembly contracts without executing assembly code.
.DESCRIPTION
Read-only. Uses reflection-only loading; never creates target objects or invokes target members.
AssemblyPath identifies the DLL. TypeName selects exact public contracts.
DependencyDirectory supplies explicit local metadata resolution folders.
VerifyConstructorOnlyProperty checks a public getter's direct readonly-field metadata only;
it does not read private field values or establish runtime command/reply behavior.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath,
    [Parameter(Mandatory = $true)]
    [ValidateCount(1, 32)]
    [string[]]$TypeName,
    [string[]]$DependencyDirectory = @(),
    [string[]]$VerifyConstructorOnlyProperty = @(),
    [ValidateRange(1, 1000)]
    [int]$MaxMembers = 100
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -eq 'Core') {
    throw 'Reflection-only metadata loading requires Windows PowerShell on .NET Framework.'
}
$assemblyFile = (Resolve-Path -LiteralPath $AssemblyPath).ProviderPath
if (-not (Test-Path -LiteralPath $assemblyFile -PathType Leaf)) {
    throw 'AssemblyPath must identify an existing assembly file.'
}
$folders = @([IO.Path]::GetDirectoryName($assemblyFile))
foreach ($directory in $DependencyDirectory) {
    $resolved = (Resolve-Path -LiteralPath $directory).ProviderPath
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw 'Each DependencyDirectory must identify an existing directory.'
    }
    $folders += $resolved
}
$folders = @($folders | Select-Object -Unique)

function Get-ContractTypeName([Type]$Type) {
    if ($null -ne $Type.FullName) { return $Type.FullName }
    return $Type.Name
}

$resolverBlock = {
    param($sender, $eventArgs)
    $name = New-Object Reflection.AssemblyName($eventArgs.Name)
    foreach ($folder in $folders) {
        $path = Join-Path $folder ($name.Name + '.dll')
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return [Reflection.Assembly]::ReflectionOnlyLoadFrom($path)
        }
    }
    return [Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name)
}
$resolver = [ResolveEventHandler]$resolverBlock
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($resolver)
try {
    $assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($assemblyFile)
    $contracts = @(foreach ($name in $TypeName) {
        $type = $assembly.GetType($name, $true)
        if (-not $type.IsVisible) { throw ('Requested type is not public: ' + $name) }
        $properties = @($type.GetProperties([Reflection.BindingFlags]'Instance,Static,Public'))
        $methods = @($type.GetMethods([Reflection.BindingFlags]'Instance,Static,Public') | Where-Object {
            -not $_.IsSpecialName -and $_.DeclaringType.FullName -ne 'System.Object'
        })
        $events = @($type.GetEvents([Reflection.BindingFlags]'Instance,Static,Public'))
        if ($properties.Count + $methods.Count + $events.Count -gt $MaxMembers) {
            throw ('Public contract exceeds MaxMembers: ' + $name)
        }
        $slotChecks = @(foreach ($propertyName in $VerifyConstructorOnlyProperty) {
            $matches = @($properties | Where-Object { $_.Name -eq $propertyName })
            if ($matches.Count -ne 1) {
                throw ('Constructor-only property is missing or ambiguous: ' + $propertyName)
            }
            $property = $matches[0]
            $getter = $property.GetMethod
            if (-not $getter -or -not $getter.IsPublic) {
                throw ('Constructor-only property has no public getter: ' + $propertyName)
            }
            $body = $getter.GetMethodBody()
            $bytes = if ($body) { $body.GetILAsByteArray() } else { @() }
            $direct = $bytes.Length -eq 7 -and $bytes[0] -eq 2 -and
                $bytes[1] -eq 123 -and $bytes[6] -eq 42
            $constructorOnly = $false
            if ($direct -and -not $getter.IsVirtual -and -not $getter.IsStatic -and
                -not $property.CanWrite -and $property.GetIndexParameters().Length -eq 0) {
                # ldarg.0; ldfld token; ret: inspect field metadata, never its value.
                $field = $getter.Module.ResolveField([BitConverter]::ToInt32($bytes, 2))
                $constructorOnly = $field.IsInitOnly -and -not $field.IsStatic -and
                    $field.DeclaringType -eq $getter.DeclaringType -and
                    $field.FieldType -eq $property.PropertyType
            }
            [pscustomobject]@{
                Property = $propertyName
                DirectFieldGetter = $direct
                ConstructorOnlySlot = [bool]$constructorOnly
            }
        })
        [pscustomobject]@{
            Type = $type.FullName
            Assembly = $type.Assembly.GetName().Name
            AssemblyIdentity = $type.Assembly.FullName
            Interfaces = @($type.GetInterfaces() | ForEach-Object { Get-ContractTypeName $_ })
            ConstructorOnlyPropertyChecks = $slotChecks
            Properties = @($properties | Sort-Object Name | ForEach-Object {
                [pscustomobject]@{
                    Name = $_.Name
                    Type = Get-ContractTypeName $_.PropertyType
                    GetterPublic = [bool]($_.GetMethod -and $_.GetMethod.IsPublic)
                    SetterPublic = [bool]($_.SetMethod -and $_.SetMethod.IsPublic)
                    IndexParameters = @($_.GetIndexParameters() | ForEach-Object {
                        Get-ContractTypeName $_.ParameterType
                    })
                }
            })
            Methods = @($methods | Sort-Object Name | ForEach-Object {
                [pscustomobject]@{
                    Name = $_.Name
                    ReturnType = Get-ContractTypeName $_.ReturnType
                    Parameters = @($_.GetParameters() | ForEach-Object {
                        [pscustomobject]@{
                            Name = $_.Name
                            Type = Get-ContractTypeName $_.ParameterType
                            Optional = $_.IsOptional
                        }
                    })
                }
            })
            Events = @($events | Sort-Object Name | ForEach-Object {
                [pscustomobject]@{ Name = $_.Name; Type = Get-ContractTypeName $_.EventHandlerType }
            })
        }
    })
    [pscustomobject]@{
        Assembly = $assembly.GetName().Name
        AssemblyIdentity = $assembly.FullName
        Contracts = $contracts
    } | ConvertTo-Json -Depth 8
}
finally {
    [AppDomain]::CurrentDomain.remove_ReflectionOnlyAssemblyResolve($resolver)
}
