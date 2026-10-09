[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskExe = Join-Path $taskRoot 'artifacts\launcher\MCG.Launcher.exe'
if (-not (Test-Path -LiteralPath $taskExe -PathType Leaf)) { throw 'Compile o launcher antes de testar a inicialização.' }
if (Get-Process -Name 'MCG.Launcher' -ErrorAction SilentlyContinue) {
    throw 'Feche o launcher antes de executar esta verificação, para testar uma nova instância.'
}

$taskProcess = Start-Process -FilePath $taskExe -WorkingDirectory (Split-Path -Parent $taskExe) -WindowStyle Hidden -PassThru
try {
    if ($taskProcess.WaitForExit(10000)) {
        throw "O launcher encerrou durante a inicialização (código $($taskProcess.ExitCode)). Consulte o log Application do Windows."
    }
    $taskProcess.Refresh()
    if ($taskProcess.MainWindowHandle -eq [IntPtr]::Zero -or -not $taskProcess.Responding) {
        throw 'O processo iniciou, mas não criou uma janela que responde.'
    }
    Write-Host "Inicialização OK: janela '$($taskProcess.MainWindowTitle)' criada e respondendo após 10 segundos."
}
finally {
    $taskProcess.Refresh()
    if (-not $taskProcess.HasExited) {
        if ($taskProcess.CloseMainWindow()) {
            if (-not $taskProcess.WaitForExit(5000)) { Write-Warning 'A janela não fechou no prazo; feche o launcher manualmente.' }
        }
        else { Write-Warning 'Não foi possível solicitar o fechamento normal; feche o launcher manualmente.' }
    }
    $taskProcess.Dispose()
}
