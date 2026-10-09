[CmdletBinding()]
param([string]$OutputDirectory = 'artifacts\launcher')

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskProject = Join-Path $taskRoot 'src\MCG.Launcher\MCG.Launcher.csproj'
$taskPublish = if ([IO.Path]::IsPathFullyQualified($OutputDirectory)) { $OutputDirectory } else { Join-Path $taskRoot $OutputDirectory }
$taskVswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

if (-not (Test-Path -LiteralPath $taskVswhere)) {
    throw 'Instale o Visual Studio Build Tools com as ferramentas de build para WinUI.'
}
$taskMsbuild = & $taskVswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $taskMsbuild) { throw 'MSBuild não encontrado. Verifique os componentes do Build Tools.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Instale o .NET SDK 10.' }

# Refuse to mix a new resource index with old assemblies locked by a running launcher.
$taskExe = Join-Path $taskPublish 'MCG.Launcher.exe'
$taskRunning = Get-Process -Name 'MCG.Launcher' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $taskExe }
if ($taskRunning) { throw 'Feche o launcher antes de atualizar esta pasta ou escolha outra pasta com -OutputDirectory.' }

Push-Location $taskRoot
try {
    & $taskMsbuild $taskProject /restore /t:Publish /m /nologo /verbosity:minimal /p:Configuration=Release /p:Platform=x64 "/p:PublishDir=$taskPublish/"
    if ($LASTEXITCODE -ne 0) { throw "Compilação falhou (código $LASTEXITCODE)." }
    # Compilation alone does not ensure that WinUI can load the published interface.
    foreach ($taskRequiredFile in @('MCG.Launcher.exe', 'MCG.Launcher.pri', 'Microsoft.UI.Xaml.dll', 'coreclr.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $taskPublish $taskRequiredFile) -PathType Leaf)) {
            throw "Publicação incompleta: $taskRequiredFile ausente. Verifique a geração dos recursos WinUI."
        }
    }
    Write-Host "Aplicativo compilado: $taskPublish\MCG.Launcher.exe"
    Write-Host 'Mantenha todos os arquivos dessa pasta junto ao executável.'
}
finally { Pop-Location }
