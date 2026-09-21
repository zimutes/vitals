<#
    Instala o Vitals.

    Compila, copia para %LOCALAPPDATA%\Programs\Vitals (fora do repositório, para não
    desaparecer num "git clean"), cria o atalho no menu Iniciar, põe o comando de terminal
    no PATH e agenda o arranque com o Windows.

    O arranque é feito por tarefa agendada com privilégios, e não pela chave Run: sem
    elevação perdem-se as temperaturas dos discos, que precisam de acesso ao SMART.

    Uso:
        .\install.ps1                  instala ou actualiza
        .\install.ps1 -SemArranque     não agenda o arranque com o Windows
        .\install.ps1 -SemPath         não mexe no PATH
#>

[CmdletBinding()]
param(
    [switch]$SemArranque,
    [switch]$SemPath
)

$ErrorActionPreference = "Stop"

$raiz = $PSScriptRoot
$destino = Join-Path $env:LOCALAPPDATA "Programs\Vitals"
$menu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"

function Passo($texto) { Write-Host "  $texto" -ForegroundColor Cyan }
function Feito($texto) { Write-Host "  $texto" -ForegroundColor Green }
function Aviso($texto) { Write-Host "  $texto" -ForegroundColor Yellow }

Write-Host "`nVitals — instalação`n" -ForegroundColor White

# ---------- 1. compilar ----------

# fechar primeiro: com o programa a correr, o ficheiro fica bloqueado, a publicação falha
# e o que segue copia o binário antigo sem se queixar
$emUso = Get-Process Vitals -ErrorAction SilentlyContinue
if ($emUso) { Passo "a fechar a versão em execução..."; $emUso | Stop-Process -Force; Start-Sleep -Seconds 1 }

Passo "a compilar..."
Push-Location $raiz
try {
    dotnet publish Sensores.Widget -c Release -r win-x64 -p:SelfContained=false `
        -p:PublishSingleFile=true -o dist/widget -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "a compilação do widget falhou" }

    dotnet publish Sensores.Cli -c Release -r win-x64 -p:SelfContained=false `
        -p:PublishSingleFile=true -o dist/cli -v q --nologo | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "a compilação do comando de terminal falhou" }
}
finally { Pop-Location }
Feito "compilado"

# ---------- 2. copiar ----------

New-Item -ItemType Directory -Force -Path $destino, "$destino\cli" | Out-Null
Copy-Item "$raiz\dist\widget\*" $destino -Recurse -Force
Copy-Item "$raiz\dist\cli\*" "$destino\cli" -Recurse -Force
Feito "instalado em $destino"

# ---------- 3. menu Iniciar ----------

$atalho = Join-Path $menu "Vitals.lnk"
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($atalho)
$link.TargetPath = "$destino\Vitals.exe"
$link.WorkingDirectory = $destino
$link.Description = "Temperaturas da gráfica, CPU, memória e discos"
$link.Save()
Feito "atalho no menu Iniciar"

# ---------- 4. comando de terminal ----------

if (-not $SemPath) {
    $pasta = "$destino\cli"
    $path = [Environment]::GetEnvironmentVariable("Path", "User")
    if (($path -split ';') -notcontains $pasta) {
        [Environment]::SetEnvironmentVariable("Path", ($path.TrimEnd(';') + ";" + $pasta), "User")
        Feito "comando 'vitals' acrescentado ao PATH (abre um terminal novo)"
    }
    else { Feito "comando 'vitals' já estava no PATH" }
}

# ---------- 5. arranque com o Windows ----------

if (-not $SemArranque) {
    $elevado = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)

    if ($elevado) {
        $opcoes = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -ExecutionTimeLimit ([TimeSpan]::Zero)
        $gatilho = New-ScheduledTaskTrigger -AtLogOn -User "$env:USERDOMAIN\$env:USERNAME"
        $accao = New-ScheduledTaskAction -Execute "$destino\Vitals.exe"

        Register-ScheduledTask -TaskName "Vitals" -Action $accao -Trigger $gatilho `
            -Settings $opcoes -RunLevel Highest -Force | Out-Null

        # a chave Run arrancaria sem elevação e sem temperaturas de disco
        Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
            -Name "Vitals" -ErrorAction SilentlyContinue

        Feito "arranque com o Windows agendado, com privilégios"
    }
    else {
        Aviso "sem privilégios: o arranque fica pela chave Run, e as temperaturas dos"
        Aviso "discos não aparecem. Corre isto num terminal de administrador para as teres."
        $run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
        New-ItemProperty $run -Name "Vitals" -Value "`"$destino\Vitals.exe`"" -PropertyType String -Force | Out-Null
    }
}

# ---------- 6. arrancar ----------

Start-Process "$destino\Vitals.exe"

Write-Host "`nPronto." -ForegroundColor White
Write-Host "  widget:   $destino\Vitals.exe (a correr, junto ao relógio)"
Write-Host "  terminal: vitals   (num terminal novo)"
Write-Host "  remover:  .\uninstall.ps1`n"
