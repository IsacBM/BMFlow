param(
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([String]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts\verification'
} elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$script:passed = 0
$script:failures = New-Object Collections.Generic.List[string]

function Pass([string] $message) {
    $script:passed++
    Write-Host "[OK] $message" -ForegroundColor Green
}

function Fail([string] $message) {
    $script:failures.Add($message)
    Write-Host "[ERRO] $message" -ForegroundColor Red
}

function Assert-Condition([bool] $condition, [string] $message) {
    if ($condition) { Pass $message } else { Fail $message }
}

function Relative-Path([string] $path) {
    $rootWithSeparator = $repositoryRoot.TrimEnd('\') + '\'
    if ($path.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        return $path.Substring($rootWithSeparator.Length)
    }
    return $path
}

Write-Host 'B.M Flow - Guardião Automático' -ForegroundColor Cyan
Write-Host "Repositório: $repositoryRoot"
Write-Host "Artefatos:   $OutputDirectory"

$requiredFiles = @(
    'BMFlow.csproj',
    'build.ps1',
    'README.md',
    'LICENSE',
    'CHANGELOG.md',
    'THIRD_PARTY_NOTICES.md',
    '.github\CONTRIBUTING.md',
    '.github\CODE_OF_CONDUCT.md',
    '.github\SECURITY.md',
    '.github\SUPPORT.md',
    '.github\workflows\ci.yml',
    'docs\ARCHITECTURE.md',
    'docs\DEVELOPMENT.md',
    'docs\ASSETS.md',
    'Assets\Images\windows11.png'
)

foreach ($relativePath in $requiredFiles) {
    Assert-Condition (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Leaf) "Arquivo obrigatório: $relativePath"
}

$requiredDirectories = @(
    'src\Dock',
    'src\Settings',
    'src\Mascots',
    'src\Workspaces',
    'src\Preview',
    'src\Cursors',
    'src\Widgets',
    'src\Models',
    'src\Properties',
    'src\WindowsInterop',
    'Assets',
    'Mascots',
    'Cursors'
)

foreach ($relativePath in $requiredDirectories) {
    Assert-Condition (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Container) "Diretório obrigatório: $relativePath"
}

Assert-Condition (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot 'DockBar.cs'))) 'O arquivo monolítico DockBar.cs não voltou ao projeto'

$dockPlacementPath = Join-Path $repositoryRoot 'src\Dock\DockPlacement.cs'
Assert-Condition (Test-Path -LiteralPath $dockPlacementPath -PathType Leaf) 'Posicionamento da Dock permanece isolado em DockPlacement.cs'

$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'src') -Filter '*.cs' -File -Recurse)
Assert-Condition ($sourceFiles.Count -ge 12) "Código modular possui ao menos 12 arquivos C# (encontrados: $($sourceFiles.Count))"

$outsideSource = @(Get-ChildItem -LiteralPath $repositoryRoot -Filter '*.cs' -File -Recurse | Where-Object {
    $_.FullName -notlike ((Join-Path $repositoryRoot 'src') + '\*') -and
    $_.FullName -notlike ((Join-Path $repositoryRoot 'bin') + '\*') -and
    $_.FullName -notlike ((Join-Path $repositoryRoot 'obj') + '\*')
})
Assert-Condition ($outsideSource.Count -eq 0) 'Não existem fontes C# fora de src/'
if ($outsideSource.Count -gt 0) {
    foreach ($file in $outsideSource) { Write-Host "       $(Relative-Path $file.FullName)" -ForegroundColor DarkRed }
}

$allSourceText = [String]::Join("`n", @($sourceFiles | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }))
$dockPlacementText = if (Test-Path -LiteralPath $dockPlacementPath) { [IO.File]::ReadAllText($dockPlacementPath) } else { '' }
foreach ($position in @('Inferior', 'Superior', 'Esquerda', 'Direita')) {
    Assert-Condition ($dockPlacementText.Contains('"' + $position + '"')) "Posição da Dock disponível: $position"
}
Assert-Condition ($allSourceText.Contains('"DockPosition="')) 'Posição da Dock é persistida nas configurações'
$expectedClasses = @(
    'App',
    'DockWindow',
    'WindowCommands',
    'PreviewWindow',
    'MacCursorService',
    'WorkspaceAppItem',
    'DockWorkspace',
    'WorkspaceManager',
    'WorkspaceHudWindow',
    'DesktopWidgetWindow',
    'DockMascot',
    'DockSettingsWindow',
    'TaskApp',
    'RecentDestination',
    'AudioController'
)

foreach ($className in $expectedClasses) {
    $count = [regex]::Matches($allSourceText, "\bclass\s+$([regex]::Escape($className))\b").Count
    if ($className -eq 'DockWindow') {
        $partialCount = [regex]::Matches($allSourceText, "\bpartial\s+class\s+$([regex]::Escape($className))\b").Count
        Assert-Condition ($count -ge 1 -and $partialCount -eq $count) "Classe $className possui somente declarações partial válidas ($count parte(s))"
    } else {
        Assert-Condition ($count -eq 1) "Classe $className aparece exatamente uma vez"
    }
}

try {
    [xml] $project = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'BMFlow.csproj')
    Assert-Condition ($project.Project.PropertyGroup.TargetFramework -eq 'net48') 'BMFlow.csproj usa .NET Framework 4.8'
    Assert-Condition ($project.Project.PropertyGroup.UseWPF -eq 'true') 'BMFlow.csproj mantém WPF habilitado'
    Assert-Condition ($project.Project.PropertyGroup.AssemblyName -eq 'BMFlow') 'BMFlow.csproj gera o assembly BMFlow'
} catch {
    Fail "BMFlow.csproj não é um XML válido: $($_.Exception.Message)"
}

$excludedDirectoryNames = @('.git', 'artifacts', 'bin', 'obj', '.vs')
$textExtensions = @('.cs', '.ps1', '.md', '.yml', '.yaml', '.xml', '.csproj', '.editorconfig', '.gitattributes', '.gitignore')
$textNames = @('LICENSE')
$textFiles = @(Get-ChildItem -LiteralPath $repositoryRoot -File -Recurse -Force | Where-Object {
    $file = $_
    $relative = Relative-Path $file.FullName
    $parts = $relative -split '[\\/]'
    $excluded = $false
    foreach ($part in $parts) {
        if ($excludedDirectoryNames -contains $part) { $excluded = $true; break }
    }
    (-not $excluded) -and (($textExtensions -contains $file.Extension.ToLowerInvariant()) -or ($textNames -contains $file.Name))
})

$trailingWhitespace = New-Object Collections.Generic.List[string]
foreach ($file in $textFiles) {
    $lines = [IO.File]::ReadAllLines($file.FullName)
    for ($index = 0; $index -lt $lines.Length; $index++) {
        if ($lines[$index] -match '[ \t]+$') {
            $trailingWhitespace.Add("$(Relative-Path $file.FullName):$($index + 1)")
        }
    }
}
Assert-Condition ($trailingWhitespace.Count -eq 0) 'Arquivos de texto não possuem espaços no fim das linhas'
foreach ($item in $trailingWhitespace) { Write-Host "       $item" -ForegroundColor DarkRed }

$brokenLinks = New-Object Collections.Generic.List[string]
$markdownFiles = @($textFiles | Where-Object { $_.Extension -eq '.md' })
foreach ($file in $markdownFiles) {
    $content = [IO.File]::ReadAllText($file.FullName)
    $matches = [regex]::Matches($content, '\[[^\]]+\]\(([^)]+)\)')
    foreach ($match in $matches) {
        $target = $match.Groups[1].Value.Trim()
        if ($target.StartsWith('<') -and $target.EndsWith('>')) {
            $target = $target.Substring(1, $target.Length - 2)
        }
        if ($target -match '^(https?://|mailto:|#)') { continue }
        $target = ($target -split '#', 2)[0]
        $target = ($target -split '\?', 2)[0]
        if ([String]::IsNullOrWhiteSpace($target)) { continue }
        try { $target = [Uri]::UnescapeDataString($target) } catch { }
        $resolvedTarget = [IO.Path]::GetFullPath((Join-Path $file.DirectoryName $target))
        if (-not (Test-Path -LiteralPath $resolvedTarget)) {
            $brokenLinks.Add("$(Relative-Path $file.FullName) -> $target")
        }
    }
}
Assert-Condition ($brokenLinks.Count -eq 0) 'Links locais da documentação apontam para arquivos existentes'
foreach ($item in $brokenLinks) { Write-Host "       $item" -ForegroundColor DarkRed }

$gitCommand = Get-Command git -ErrorAction SilentlyContinue
$statusBefore = $null
if ($gitCommand) {
    $statusBefore = [String]::Join("`n", @(& git -C $repositoryRoot status --porcelain))
    $trackedFiles = @(& git -C $repositoryRoot ls-files)
    $invalidTrackedFiles = @($trackedFiles | Where-Object {
        $_ -match '(^|/)(bin|obj|artifacts|TestResults)/' -or
        $_ -match '(^|/)crash\.log$' -or
        $_ -match '(^|/)\.tmp-build-check/'
    })
    Assert-Condition ($invalidTrackedFiles.Count -eq 0) 'Git não rastreia saídas temporárias ou diretórios de build'
    foreach ($item in $invalidTrackedFiles) { Write-Host "       $item" -ForegroundColor DarkRed }
}

try {
    if (-not (Test-Path -LiteralPath $OutputDirectory)) {
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    }
    $artifactPath = Join-Path $OutputDirectory 'BMFlow.exe'
    & (Join-Path $repositoryRoot 'build.ps1') -OutputPath $artifactPath
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 retornou o código $LASTEXITCODE" }

    Assert-Condition (Test-Path -LiteralPath $artifactPath -PathType Leaf) 'Build gerou BMFlow.exe'
    if (Test-Path -LiteralPath $artifactPath -PathType Leaf) {
        $executable = Get-Item -LiteralPath $artifactPath
        $version = $executable.VersionInfo
        Assert-Condition ($executable.Length -gt 500000) "Executável possui tamanho plausível ($($executable.Length) bytes)"
        Assert-Condition ($version.ProductName -eq 'B.M Flow') 'Executável identifica o produto como B.M Flow'
        Assert-Condition (-not [String]::IsNullOrWhiteSpace($version.FileVersion) -and $version.FileVersion -ne '0.0.0.0') "Executável possui versão válida ($($version.FileVersion))"

        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifactPath).Hash
        $hashPath = Join-Path $OutputDirectory 'BMFlow.exe.sha256'
        $utf8WithoutBom = New-Object Text.UTF8Encoding($false)
        [IO.File]::WriteAllText($hashPath, "$hash  BMFlow.exe`r`n", $utf8WithoutBom)
        Pass "SHA-256 gerado: $hash"
    }
} catch {
    Fail "Build portátil falhou: $($_.Exception.Message)"
}

if ($gitCommand) {
    $statusAfter = [String]::Join("`n", @(& git -C $repositoryRoot status --porcelain))
    Assert-Condition ($statusAfter -eq $statusBefore) 'Verificação não altera arquivos rastreados no repositório'
}

Write-Host ''
if ($script:failures.Count -gt 0) {
    Write-Host "Guardião encontrou $($script:failures.Count) problema(s):" -ForegroundColor Red
    foreach ($failure in $script:failures) { Write-Host " - $failure" -ForegroundColor Red }
    exit 1
}

Write-Host "Guardião aprovado: $script:passed verificações concluídas." -ForegroundColor Green
Write-Host "Artefatos disponíveis em: $OutputDirectory" -ForegroundColor Green
