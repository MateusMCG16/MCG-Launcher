[CmdletBinding()]
param([string]$Executable = 'artifacts\launcher\MCG.Launcher.exe')

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskExe = if ([IO.Path]::IsPathFullyQualified($Executable)) { $Executable } else { Join-Path $taskRoot $Executable }
if (Get-Process -Name 'MCG.Launcher' -ErrorAction SilentlyContinue) { throw 'Feche o launcher antes de verificar o modal.' }
$taskReports = Join-Path $taskRoot 'artifacts\checks'
[IO.Directory]::CreateDirectory($taskReports) | Out-Null
$taskReport = Join-Path $taskReports ("settings-dialog-" + [guid]::NewGuid().ToString('N') + '.json')
$taskProcess = Start-Process -FilePath $taskExe -WorkingDirectory (Split-Path -Parent $taskExe) -ArgumentList @('--check-settings-dialog', ('"' + $taskReport + '"')) -WindowStyle Hidden -PassThru
try {
    if (-not $taskProcess.WaitForExit(20000)) { throw 'A verificação não terminou em 20 segundos.' }
    if (-not (Test-Path -LiteralPath $taskReport)) { throw "O launcher não gerou o relatório (código $($taskProcess.ExitCode))." }
    $taskResult = Get-Content -LiteralPath $taskReport -Raw | ConvertFrom-Json
    if (-not $taskResult.Opened -or -not $taskResult.Rendered -or $taskResult.Error) { throw "Falha no modal: $($taskResult.Error)" }
    Write-Output 'Modal OK: Configurações abriu e a opção Desativar imagens foi renderizada.'
    Write-Output "Relatório preservado: $taskReport"
}
finally { $taskProcess.Dispose() }
