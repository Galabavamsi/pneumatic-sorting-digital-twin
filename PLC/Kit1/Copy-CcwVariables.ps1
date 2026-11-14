<#
.SYNOPSIS
  Puts Kit 1 variables on the Windows clipboard in the XML layout that the
  Connected Components Workbench variable grid copies and pastes.

.DESCRIPTION
  CCW copies a selected variable row as plain-text XML (<Variables><Variable>...).
  Pasting the same layout into the grid creates the variables, so nobody has to
  type them. The variables come from Kit1_Variables.csv next to this script.

  Layout taken from CCW 22 on 2026-11-14 (copy of a single INT global).

  Names are not case-sensitive in CCW.

.EXAMPLE
  # Global Variables grid: click the empty bottom row, run this, then Ctrl+V
  .\Copy-CcwVariables.ps1 -Scope Global

.EXAMPLE
  # Local Variables grid of the Kit1_Sort program (the prefix ends with the program name)
  .\Copy-CcwVariables.ps1 -Scope Local -FullNamePrefix 'Controller.Micro850.Micro850.Kit1_Sort'

.EXAMPLE
  # Leave out variables that already exist in the grid
  .\Copy-CcwVariables.ps1 -Scope Local -FullNamePrefix 'Controller.Micro850.Micro850.Kit1_Sort' -Exclude stepPrev
#>
param(
    [Parameter(Mandatory = $true)][ValidateSet('Global', 'Local')][string]$Scope,
    [string[]]$Exclude = @(),
    [string]$FullNamePrefix = 'Controller.Micro850.Micro850',
    [string]$CsvPath = (Join-Path $PSScriptRoot 'Kit1_Variables.csv'),
    [string]$OutFile,
    [switch]$UpperCase,
    [switch]$NoClipboard
)

$ErrorActionPreference = 'Stop'
$nl = "`r`n"
$empty = { param($tag) "    <$tag>$nl    </$tag>$nl" }
$excludeUpper = $Exclude | ForEach-Object { $_.ToUpperInvariant() }

$rows = Import-Csv -Path $CsvPath | Where-Object {
    $_.scope -eq $Scope -and ($excludeUpper -notcontains $_.name.ToUpperInvariant())
}
if (-not $rows) { throw "No $Scope variables found in $CsvPath." }

$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('<?xml version="1.0" encoding="UTF-8"?>' + $nl)
[void]$sb.Append('<Variables version="1.0">' + $nl)
foreach ($row in $rows) {
    $name = if ($UpperCase) { $row.name.ToUpperInvariant() } else { $row.name }
    $initial = $row.initial_value
    # CCW's own defaults are FALSE / 0, so only write an initial value that differs.
    if ($initial -eq 'FALSE' -or $initial -eq '0') { $initial = '' }

    [void]$sb.Append("  <Variable>$nl")
    [void]$sb.Append("    <FullName>$FullNamePrefix.$name</FullName>$nl")
    [void]$sb.Append("    <Name>$name</Name>$nl")
    [void]$sb.Append("    <TextDataType>$($row.data_type)</TextDataType>$nl")
    [void]$sb.Append((& $empty 'Dimension'))
    [void]$sb.Append((& $empty 'StringSize'))
    if ($initial) {
        [void]$sb.Append("    <TextInitialValue>$initial</TextInitialValue>$nl")
    } else {
        [void]$sb.Append((& $empty 'TextInitialValue'))
    }
    [void]$sb.Append("    <Direction>Var</Direction>$nl")
    [void]$sb.Append("    <Attribute>ReadWrite</Attribute>$nl")
    [void]$sb.Append((& $empty 'Comment'))
    [void]$sb.Append((& $empty 'Alias'))
    [void]$sb.Append("    <TextLogicalValue>OFFLINE</TextLogicalValue>$nl")
    [void]$sb.Append("    <TextPhysicalValue>OFFLINE</TextPhysicalValue>$nl")
    [void]$sb.Append("    <Lock>False</Lock>$nl")
    [void]$sb.Append((& $empty 'TextCommentFields'))
    [void]$sb.Append((& $empty 'TextLocalLogValue'))
    [void]$sb.Append("  </Variable>$nl")
}
[void]$sb.Append("  <DataTypes />$nl")
[void]$sb.Append('</Variables>')
$xml = $sb.ToString()

if ($OutFile) { [System.IO.File]::WriteAllText($OutFile, $xml, (New-Object System.Text.UTF8Encoding($false))) }
if (-not $NoClipboard) { Set-Clipboard -Value $xml }

$byType = ($rows | Group-Object data_type | ForEach-Object { "$($_.Count) $($_.Name)" }) -join ', '
"$(@($rows).Count) $Scope variables ($byType)" + $(if ($NoClipboard) { '' } else { ' are on the clipboard' })
