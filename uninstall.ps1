<#
    Remove o Vitals: fecha-o, apaga a instalação, o atalho, a tarefa de arranque e a
    entrada no PATH.

    As preferências em %APPDATA%\Vitals ficam, a não ser que se use -All — assim
    uma reinstalação volta a encontrar as linhas escolhidas e a posição da janela.
#>

[CmdletBinding()]
param([Alias("Tudo")][switch]$All)

$ErrorActionPreference = "Continue"

$destino = Join-Path $env:LOCALAPPDATA "Programs\Vitals"
$atalho = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Vitals.lnk"
$preferencias = Join-Path $env:APPDATA "Vitals"

function Feito($texto) { Write-Host "  $texto" -ForegroundColor Green }

Write-Host "`nVitals — remoção`n" -ForegroundColor White

Get-Process Vitals -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700

if (Test-Path $atalho) { Remove-Item $atalho -Force; Feito "atalho removido" }

if (Get-ScheduledTask -TaskName "Vitals" -ErrorAction SilentlyContinue) {
    Unregister-ScheduledTask -TaskName "Vitals" -Confirm:$false
    Feito "tarefa de arranque removida"
}

Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "Vitals" -ErrorAction SilentlyContinue

$pasta = "$destino\cli"
$path = [Environment]::GetEnvironmentVariable("Path", "User")
if (($path -split ';') -contains $pasta) {
    $limpo = (($path -split ';') | Where-Object { $_ -ne $pasta }) -join ';'
    [Environment]::SetEnvironmentVariable("Path", $limpo, "User")
    Feito "PATH limpo"
}

if (Test-Path $destino) { Remove-Item $destino -Recurse -Force; Feito "ficheiros removidos" }

if ($All -and (Test-Path $preferencias)) {
    Remove-Item $preferencias -Recurse -Force
    Feito "preferências removidas"
}
elseif (Test-Path $preferencias) {
    Write-Host "  preferências mantidas em $preferencias (usa -All para as apagar)" -ForegroundColor Yellow
}

Write-Host "`nRemovido.`n" -ForegroundColor White
