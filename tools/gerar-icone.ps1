<#
    Gera o ícone da aplicação (Sensores.Widget\vitals.ico).

    O desenho é o mesmo do ícone junto ao relógio: um batimento verde sobre fundo escuro.
    Fica em código, e não num ficheiro binário opaco, para se poder mexer na cor ou na
    espessura sem abrir um editor de imagens.

        .\tools\gerar-icone.ps1
#>

[CmdletBinding()]
param([string]$Destino = "$PSScriptRoot\..\Sensores.Widget\vitals.ico")

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$tamanhos = @(16, 20, 24, 32, 48, 64, 128, 256)
$fundo = [System.Drawing.Color]::FromArgb(255, 18, 18, 22)
$traco = [System.Drawing.Color]::FromArgb(255, 134, 239, 172)

function Desenhar([int]$lado) {
    $bmp = New-Object System.Drawing.Bitmap($lado, $lado)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # disco escuro, com uma margem para não encostar às arestas
    $margem = [Math]::Max(1, [int]($lado * 0.03))
    $pincel = New-Object System.Drawing.SolidBrush($fundo)
    $g.FillEllipse($pincel, $margem, $margem, $lado - 2 * $margem - 1, $lado - 2 * $margem - 1)

    # linha de batimento, com espessura proporcional
    $caneta = New-Object System.Drawing.Pen($traco, [Math]::Max(1.2, $lado * 0.085))
    $caneta.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $caneta.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $caneta.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    # cada argumento tem de ir entre parênteses: em PowerShell a vírgula liga mais forte
    # que a multiplicação, e "5 * $u, 17 * $u" seria lido como 5 * ($u, 17) * $u
    $u = $lado / 32.0
    $pontos = @(
        (New-Object System.Drawing.PointF((5 * $u), (17 * $u))),
        (New-Object System.Drawing.PointF((11 * $u), (17 * $u))),
        (New-Object System.Drawing.PointF((14 * $u), (9 * $u))),
        (New-Object System.Drawing.PointF((18 * $u), (24 * $u))),
        (New-Object System.Drawing.PointF((21 * $u), (17 * $u))),
        (New-Object System.Drawing.PointF((27 * $u), (17 * $u)))
    )
    $g.DrawLines($caneta, $pontos)

    $caneta.Dispose(); $pincel.Dispose(); $g.Dispose()
    return $bmp
}

# cada imagem vai em PNG dentro do .ico — suportado desde o Windows Vista
$imagens = @()
foreach ($lado in $tamanhos) {
    $bmp = Desenhar $lado
    $memoria = New-Object System.IO.MemoryStream
    $bmp.Save($memoria, [System.Drawing.Imaging.ImageFormat]::Png)
    $imagens += , @{ Lado = $lado; Bytes = $memoria.ToArray() }
    $memoria.Dispose(); $bmp.Dispose()
}

$saida = New-Object System.IO.MemoryStream
$escritor = New-Object System.IO.BinaryWriter($saida)

$escritor.Write([uint16]0)                      # reservado
$escritor.Write([uint16]1)                      # tipo: ícone
$escritor.Write([uint16]$imagens.Count)

$deslocamento = 6 + 16 * $imagens.Count
foreach ($img in $imagens) {
    $escritor.Write([byte]($(if ($img.Lado -ge 256) { 0 } else { $img.Lado })))   # largura
    $escritor.Write([byte]($(if ($img.Lado -ge 256) { 0 } else { $img.Lado })))   # altura
    $escritor.Write([byte]0)                    # cores da paleta
    $escritor.Write([byte]0)                    # reservado
    $escritor.Write([uint16]1)                  # planos
    $escritor.Write([uint16]32)                 # bits por pixel
    $escritor.Write([uint32]$img.Bytes.Length)
    $escritor.Write([uint32]$deslocamento)
    $deslocamento += $img.Bytes.Length
}
foreach ($img in $imagens) { $escritor.Write($img.Bytes) }

$escritor.Flush()
[IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $Destino) | ForEach-Object { Join-Path $_ (Split-Path $Destino -Leaf) }), $saida.ToArray())
$escritor.Dispose(); $saida.Dispose()

Write-Host "ícone gerado: $Destino ($($tamanhos -join ', ') px)" -ForegroundColor Green
