param(
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$defaultOutput = Join-Path $PSScriptRoot 'BMFlow.exe'
$output = if ([String]::IsNullOrWhiteSpace($OutputPath)) {
    $defaultOutput
} elseif ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputPath))
}
$legacyOutput = Join-Path $PSScriptRoot 'DockBar.exe'
$sourceRoot = Join-Path $PSScriptRoot 'src'
$startIcon = Join-Path $PSScriptRoot 'Assets\Images\windows11.png'
$assemblyRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'O compilador do .NET Framework não foi encontrado neste Windows.'
}

$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -Recurse -File |
    Sort-Object FullName |
    Select-Object -ExpandProperty FullName

if (-not $sources) {
    throw "Nenhum arquivo C# foi encontrado em $sourceRoot."
}

$outputDirectory = Split-Path -Parent $output
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
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
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "A compilação falhou com o código $LASTEXITCODE."
}

if ($output -eq $defaultOutput -and (Test-Path -LiteralPath $legacyOutput) -and $legacyOutput -ne $output) {
    Remove-Item -LiteralPath $legacyOutput -Force
}

Write-Host "Criado: $output"
