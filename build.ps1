$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Join-Path $PSScriptRoot 'BMFlow.exe'
$legacyOutput = Join-Path $PSScriptRoot 'DockBar.exe'
$source = Join-Path $PSScriptRoot 'DockBar.cs'
$startIcon = Join-Path $PSScriptRoot 'windows11.png'
$assemblyRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'O compilador do .NET Framework não foi encontrado neste Windows.'
}

function Find-FrameworkAssembly([string] $name) {
    $assembly = Get-ChildItem -LiteralPath $assemblyRoot -Filter "$name.dll" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $assembly) {
        throw "Biblioteca $name.dll não encontrada."
    }
    return $assembly
}

$presentationCore = Find-FrameworkAssembly 'PresentationCore'
$presentationFramework = Find-FrameworkAssembly 'PresentationFramework'
$windowsBase = Find-FrameworkAssembly 'WindowsBase'
$systemXaml = Find-FrameworkAssembly 'System.Xaml'
$uiAutomationClient = Find-FrameworkAssembly 'UIAutomationClient'
$uiAutomationTypes = Find-FrameworkAssembly 'UIAutomationTypes'
$resourceArgument = "/resource:$startIcon,windows11.png"

& $compiler /nologo /target:winexe /optimize+ /codepage:65001 /utf8output /out:$output `
    $resourceArgument `
    /reference:$presentationCore `
    /reference:$presentationFramework `
    /reference:$windowsBase `
    /reference:$systemXaml `
    /reference:$uiAutomationClient `
    /reference:$uiAutomationTypes `
    /reference:System.dll `
    /reference:System.Core.dll `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "A compilação falhou com o código $LASTEXITCODE."
}

if ((Test-Path -LiteralPath $legacyOutput) -and $legacyOutput -ne $output) {
    Remove-Item -LiteralPath $legacyOutput -Force
}

Write-Host "Criado: $output"
