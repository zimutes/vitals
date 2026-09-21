# Vitals

Um widget pequeno de monitorização para Windows, e um comando de terminal que lê os mesmos
sensores. O comando de terminal também corre em Linux.

Mostra as temperaturas da gráfica — incluindo a **temperatura de junção da memória GDDR** e
a **margem até a placa estrangular** — uso do CPU, memória, e temperatura e espaço de cada
disco. É deliberadamente discreto: escolhes as linhas que queres ver, e um sensor que não
existe mesmo aparece como `n/d` em vez de um zero convincente.

*[Read me in English](README.md)*

*Notas técnicas sobre o acesso aos sensores — que API dá o quê, as armadilhas, e de onde
vem cada número — em [docs/sensor-access.md](docs/sensor-access.md) (em inglês).*

---

> ### Antes de mais
>
> **Feito para uma máquina, partilhado como está.** O Vitals foi escrito para ler os
> sensores do PC do autor, contra esse hardware e mais nenhum. **Não há garantia nenhuma de
> que funcione no teu sistema** — os sensores variam muito entre placas, fabricantes e
> controladores, e um valor que este código vai buscar a um sítio pode, no teu computador,
> estar noutro ou não existir de todo. Conta com linhas em falta. Sem garantia de espécie
> alguma, expressa ou implícita — ver [LICENSE](LICENSE).
>
> **O ponto quente da gráfica precisa do HWiNFO.** A NVIDIA não publica esse sensor em
> interface nenhuma, por isso o Vitals vai buscá-lo à memória partilhada do HWiNFO. A linha
> `hotspot` e as temperaturas de cada módulo GDDR só aparecem com o **HWiNFO 8.53 ou mais
> recente a correr, e com o *Shared Memory Support* ligado nas definições** — os pormenores
> estão [nas perguntas](#o-ponto-quente-não-vem-da-nvidia-vem-do-hwinfo). Sem isso não
> rebenta nada: essas linhas simplesmente desaparecem.
>
> **Este ficheiro foi escrito por IA** (Claude), a partir do código, e revisto pelo autor
> antes de ser publicado.

---

## Dois programas, um leitor

| | |
|---|---|
| `Vitals.exe` | Widget sem bordas: sempre à frente, arrastável, redimensionável, esconde-se junto ao relógio, arranca com o Windows. |
| `vitals.exe` | Comando de terminal: uma leitura, `--watch` ao vivo, ou `--json` para outros programas. |

## O que lê

| Leitura | Fonte | Precisa de administrador |
|---|---|---|
| Temperatura do core da GPU | NVML / API do fabricante | não |
| **Junção da memória da GPU** | API do fabricante | não |
| **Margem térmica (T.Limit)** | limites da NVML | não |
| **Ponto quente da GPU** | memória partilhada do HWiNFO | não, mas o HWiNFO tem de correr |
| Temperatura de cada módulo GDDR7 | memória partilhada do HWiNFO | não, mas o HWiNFO tem de correr |
| Uso, VRAM, potência e ventoinha da GPU | API do fabricante | não |
| Uso do CPU | contadores do Windows | não |
| Temperatura do CPU | MSR (driver de kernel) | sim, *e* driver desbloqueado — ver perguntas |
| Memória do sistema | Windows | não |
| Espaço em disco, por volume | Windows | não |
| Temperatura e actividade dos discos | SMART | **sim** |
| Bateria de portátil | Windows | não |

O que não estiver disponível aparece como `n/d` e a linha some-se. O Vitals nunca inventa
um número plausível para substituir um sensor em falta.

## Instalação em Windows

Num terminal PowerShell **como administrador**, na pasta do repositório:

```powershell
.\install.ps1
```

Compila os dois programas, instala-os em `%LOCALAPPDATA%\Programs\Vitals`, cria o atalho no
menu Iniciar, põe o `vitals` no `PATH` e agenda o arranque com o Windows. Ser administrador
importa: só assim a tarefa de arranque fica com privilégios, que é o que as temperaturas dos
discos exigem. Sem isso instala à mesma, e diz o que se perde.

O `.\uninstall.ps1` remove tudo — com `-All` apaga também as preferências.

Em Linux o instalador é outro, e chega um comando — ver [Instalação em Linux](#instalação-em-linux) mais abaixo.

## Utilização

Depois de instalado, o widget está no menu Iniciar como **Vitals**, e num terminal novo:

```
vitals                     uma leitura
vitals --watch             ao vivo, redesenha no sítio (↑↓ ou j/k para rolar, q para sair)
vitals --watch --interval 2
vitals --json              para outros programas
vitals --sensors           todos os sensores detectados, com os nomes reais
vitals --hwinfo            o que o HWiNFO está a publicar, se estiver a correr
vitals --help
```

### O widget

- Arrastar com o botão esquerdo; a posição fica guardada, e respeita o monitor onde está.
- **Pega no canto inferior direito** para redimensionar.
- **Fechar esconde junto ao relógio.** Duplo clique no ícone traz de volta; só o *Sair*
  acaba mesmo.
- Botão direito abre o menu:
  - **Mostrar** — liga e desliga cada linha. Passar o rato explica o que cada uma é, e
    *"O que é cada linha..."* abre uma janela com todas as explicações.
  - **Intervalo** — 0,5s, 1s, 2s ou 5s.
  - Sempre à frente, iniciar com o Windows, copiar leitura, ajustar ao conteúdo, repor,
    esconder, sair.
- Ao fundo: há quanto tempo o PC está ligado, a hora, o intervalo, e um ponto que pisca a
  cada leitura.

### Como se arruma quando cresce

Deixado por sua conta, dimensiona-se sozinho: uma coluna, e duas lado a lado quando o
conteúdo passa dos ~430 px de altura, sem nunca partir um grupo ao meio.

Se o redimensionares à mão, reage nas **duas** direcções:

- **A largura** decide quantas colunas, até quatro. Uma tira de 740 × 62 mostra doze
  valores em quatro colunas.
- **A altura** decide quantas linhas cabem. O que não couber é **cortado, não rolado** —
  pequeno quer dizer "estou a jogar, mostra-me o que interessa", por isso as linhas são
  ordenadas por importância (core, hotspot, delta, margem, junção, cargas, memória, discos)
  e cortadas pelo fim. A dica diz quantas ficaram de fora.
- **Pequeno ou estreito passa a compacto**: sem cabeçalho, rodapé nem títulos de secção, e
  com os espaços ao mínimo. Abaixo de 190 px de largura saem também as barras, para as
  legendas e os números continuarem legíveis.

No menu, **Densidade** força o modo compacto ou o folgado, se preferires decidir tu.

### As temperaturas dos discos exigem administrador

Ler SMART obriga a elevação. O **espaço** não, por isso sem privilégios os discos continuam
a aparecer, com o espaço certo e `n/d` na temperatura.

O widget regista-se em `HKCU\...\Run`, que arranca **sem** elevação. Para teres as
temperaturas logo no arranque, troca isso por uma tarefa agendada:

```powershell
$exe = "C:\caminho\para\dist\widget\Vitals.exe"
$acao = New-ScheduledTaskAction -Execute $exe
$gatilho = New-ScheduledTaskTrigger -AtLogOn
$opcoes = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName "Vitals" -Action $acao -Trigger $gatilho `
    -Settings $opcoes -RunLevel Highest
```

Depois desliga *Iniciar com o Windows* no menu, para não arrancar duas vezes.

---

## Instalação em Linux

Um comando, sem root e sem .NET:

```sh
curl -fsSL https://raw.githubusercontent.com/zimutes/vitals/main/install.sh | sh
```

Depois é correr `vitals`, ou `vitals --watch` para ficar a actualizar.

**O que instalas é o comando de terminal, não o widget.** O widget é WPF e só existe em
Windows; em Linux o `vitals` mostra as mesmas leituras no terminal.

O instalador descarrega o executável autónomo para a tua arquitectura, confere-o contra o
`SHA256SUMS` publicado, instala-o em `~/.local/share/vitals` e deixa o comando em
`~/.local/bin`. Com `--system` instala em `/usr/local` para
toda a gente, com `--version v1.1` fixa uma versão, e `--uninstall` remove.

### O que muda

Aqui não há LibreHardwareMonitor nem driver nenhum para carregar: o kernel publica quase
tudo em ficheiros de texto, e o `FonteLinux` lê-os directamente.

| Leitura | De onde vem | Notas |
|---|---|---|
| Temperatura do CPU | `k10temp`, `zenpower`, `coretemp` | **Funciona** — ao contrário do Windows, onde o driver costuma estar bloqueado |
| Carga e nome do CPU | `/proc/stat`, `/proc/cpuinfo` | |
| Memória | `/proc/meminfo` | |
| Gráfica AMD: core, junção, uso, VRAM | hwmon `amdgpu`, `/sys/class/drm` | A junção vem de graça: o kernel publica-a como `temp2_input` |
| Gráfica NVIDIA: core, margem térmica | hwmon `nvidia`, NVML | **O uso e a VRAM ficam em branco** — ainda não são lidos em Linux |
| Potência e ventoinha da gráfica | hwmon | |
| Temperatura dos discos | hwmon `nvme`, `drivetemp` | Os SATA precisam de `modprobe drivetemp`; a ligação ao ponto de montagem é aproximada |
| Bateria | `/sys/class/power_supply/BAT*` | |
| Ponto quente, GDDR módulo a módulo | — | Não existe: vêm do HWiNFO, que é só Windows |

Os chips da motherboard — ventoinhas da caixa, voltagens — precisam do módulo do kernel
carregado primeiro:

```sh
sudo dnf install lm_sensors      # Debian/Ubuntu: sudo apt install lm-sensors
sudo sensors-detect --auto
```

Se faltar uma linha que esperavas, o `vitals --sensors` lista todos os chips e sensores
encontrados em `/sys/class/hwmon`. É essa saída que deve acompanhar um relatório.

Vale aqui o mesmo aviso do princípio, e com mais força: o caminho de Linux foi experimentado
em duas ou três máquinas, não em muitas.

---

## Perguntas

### O ponto quente não vem da NVIDIA, vem do HWiNFO

A NVIDIA não o expõe em interface pública nenhuma. Numa RTX 5070 foi verificado pelo
`nvidia-smi`, pela LibreHardwareMonitor, por um varrimento dos campos da NVML, e pela API
privada `ThermChannelGetStatus` mapeada bit a bit — esta última devolve exactamente dois
canais: core e junção de memória.

**O HWiNFO lê-o à mesma**, com driver de kernel próprio, directamente da placa — e é assim
que consegue também uma temperatura para cada módulo GDDR7. O Vitals lê a memória
partilhada do HWiNFO (`HWiNFO_SENS_SM2`) quando está disponível, e mostra a linha
`hotspot` e uma `delta` de hotspot menos core. É preciso:

- **HWiNFO 8.53 ou mais recente** — as versões anteriores não têm o sensor nas Blackwell;
- a correr, com *Suporte de memória compartilhada* ligado (no gratuito dura 12 horas por
  arranque).

Sem isso não parte nada: a linha desaparece e a `delta` volta a ser junção menos core. O
comando `vitals --hwinfo` mostra exactamente o que está a ser publicado.

O outro número que o controlador publica é o **T.Limit**: quantos graus faltam até
estrangular.

O Vitals calcula essa margem a partir dos limites da própria placa:

```
gpu max operating  85 °C   ← começa a reduzir desempenho
slowdown           87 °C
shutdown           90 °C
```

Confirmado duas vezes contra o `nvidia-smi`: com o core a 74 °C o T.Limit era 11 (85−74);
a 48 °C era 37 (85−48). Estes limites servem também para as cores da linha do core, em vez
de limiares inventados.

A **junção da memória** — a que interessa mesmo para GDDR6/7 — essa existe e é mostrada.

### Porque não há temperatura do CPU?

Ler o Tctl/Tdie de um Ryzen (ou o `CPU Package` num Intel) exige acesso aos registos MSR,
que só se faz por um driver de kernel. O `WinRing0`, usado pela biblioteca de sensores, está
na lista de drivers vulneráveis da Microsoft, por isso em sistemas actualizados recusa-se a
carregar e todas as temperaturas do CPU dão `0,0`.

O Vitals trata um zero como ausência de leitura e esconde a linha, em vez de mostrar um
valor falso. Em máquinas onde o driver carrega, a linha aparece sozinha. Desactivar a lista
de bloqueio não é uma solução recomendável.

### Quanto custa a correr?

Cerca de 6–7 % de um núcleo com intervalo de um segundo, e uns 190 MB de memória. Pôr o
intervalo a 2 segundos corta o CPU para quase metade. Os discos são relidos de 10 em 10
segundos de qualquer forma, porque o SMART é caro e as temperaturas mudam devagar.

---

## Compilar

```
dotnet build
dotnet publish Sensores.Widget -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o dist/widget
dotnet publish Sensores.Cli    -c Release -r win-x64 -p:SelfContained=false -p:PublishSingleFile=true -o dist/cli
```

Em Linux só o comando de terminal compila, e autónomo, para levar o runtime consigo:

```sh
dotnet publish Sensores.Cli -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -o dist/linux
```

Cada executável de Windows fica com ~3,5 MB e precisa do runtime .NET 8 Desktop. Trocar
`SelfContained=false` por `true` dá um executável autónomo de ~150 MB que corre em qualquer
lado — é o que as versões publicadas levam, construídas pelo
[.github/workflows/release.yml](.github/workflows/release.yml) a cada etiqueta `v*`.

```
Sensores.Core/      leitura dos sensores, partilhada
Sensores.Widget/    widget WPF            → Vitals.exe
Sensores.Cli/       comando de terminal   → vitals.exe
```

O nome e a assinatura estão em `Sensores.Core/Marca.cs`, num sítio só.

## Licença

MIT — ver [LICENSE](LICENSE). Os componentes de terceiros, incluindo a
LibreHardwareMonitorLib (MPL-2.0), estão em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
