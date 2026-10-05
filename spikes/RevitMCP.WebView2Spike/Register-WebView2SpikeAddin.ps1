# Registers the isolated WebView2 spike with one Revit year.
# This does not register the production RevitMCP add-in.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("2025", "2026", "2027")]
    [string]$RevitVersion,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$targetFramework = if ($RevitVersion -eq "2027") { "net10.0-windows" } else { "net8.0-windows" }
$assemblyPath = Join-Path $PSScriptRoot "bin\$RevitVersion\$Configuration\$targetFramework\RevitMCP.WebView2Spike.dll"
$assemblyPath = [System.IO.Path]::GetFullPath($assemblyPath)
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Spike assembly not found: $assemblyPath. Build that Revit year first."
}

$addinDirectory = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
New-Item -ItemType Directory -Force -Path $addinDirectory | Out-Null
$addinPath = Join-Path $addinDirectory "RevitMCP.WebView2Spike.addin"
$assemblyXml = [System.Security.SecurityElement]::Escape($assemblyPath)

$manifest = @"
<?xml version="1.0" encoding="utf-8" standalone="no"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>RevitMCP WebView2 Spike</Name>
    <Assembly>$assemblyXml</Assembly>
    <AddInId>E6A0C3F4-9D57-4E81-BC32-7041AD5E86F3</AddInId>
    <FullClassName>RevitMCP.WebView2Spike.SpikeApplication</FullClassName>
    <VendorId>RVMC</VendorId>
    <VendorDescription>Isolated WebView2 feasibility spike. Not the production RevitMCP add-in.</VendorDescription>
  </AddIn>
  <AddIn Type="Command">
    <Name>RevitMCP UI Spike Show</Name>
    <Assembly>$assemblyXml</Assembly>
    <AddInId>C4E8A1D2-7B35-4C6F-9A10-5E2F8B3C64D1</AddInId>
    <FullClassName>RevitMCP.WebView2Spike.ShowSpikePaneCommand</FullClassName>
    <VendorId>RVMC</VendorId>
    <Text>RevitMCP UI Spike - Show</Text>
  </AddIn>
  <AddIn Type="Command">
    <Name>RevitMCP UI Spike Hide</Name>
    <Assembly>$assemblyXml</Assembly>
    <AddInId>D5F9B2E3-8C46-4D70-AB21-6F309C4D75E2</AddInId>
    <FullClassName>RevitMCP.WebView2Spike.HideSpikePaneCommand</FullClassName>
    <VendorId>RVMC</VendorId>
    <Text>RevitMCP UI Spike - Hide</Text>
  </AddIn>
</RevitAddIns>
"@

Set-Content -LiteralPath $addinPath -Value $manifest -Encoding utf8
Write-Host "Wrote spike manifest for Revit $RevitVersion"
Write-Host "Restart Revit $RevitVersion, then use Add-Ins > External Tools."
