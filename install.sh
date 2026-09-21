#!/bin/sh
#
#   Instala o Vitals em Linux — o comando de terminal.
#
#   Descarrega o executável da última versão publicada e põe o comando `vitals` ao alcance
#   da shell. Não precisa de root nem do .NET instalado: o executável é autónomo.
#
#   O widget não vem aqui. É WPF, só existe em Windows.
#
#   Uso:
#       curl -fsSL https://raw.githubusercontent.com/zimutes/vitals/main/install.sh | sh
#
#       ./install.sh                   instala ou actualiza, só para este utilizador
#       ./install.sh --sistema         instala em /usr/local, para todos (pede sudo)
#       ./install.sh --versao v1.1     instala uma versão determinada
#       ./install.sh --desinstalar     remove
#

set -eu

REPO="zimutes/vitals"

# ---------- aparência ----------

if [ -t 1 ] && [ -z "${NO_COLOR:-}" ]; then
    AZUL=$(printf '\033[36m');    VERDE=$(printf '\033[32m')
    AMARELO=$(printf '\033[33m'); VERMELHO=$(printf '\033[31m')
    BRANCO=$(printf '\033[97m');  FIM=$(printf '\033[0m')
else
    AZUL=''; VERDE=''; AMARELO=''; VERMELHO=''; BRANCO=''; FIM=''
fi

passo() { printf '  %s%s%s\n' "$AZUL"     "$1" "$FIM"; }
feito() { printf '  %s%s%s\n' "$VERDE"    "$1" "$FIM"; }
aviso() { printf '  %s%s%s\n' "$AMARELO"  "$1" "$FIM"; }
erro()  { printf '  %s%s%s\n' "$VERMELHO" "$1" "$FIM" >&2; exit 1; }

# ---------- argumentos ----------

sistema=0
desinstalar=0
versao=''

while [ $# -gt 0 ]; do
    case "$1" in
        --sistema)      sistema=1 ;;
        --desinstalar)  desinstalar=1 ;;
        --versao)       shift; [ $# -gt 0 ] || erro "falta a versão a seguir a --versao."; versao="$1" ;;
        --ajuda|--help|-h)
            printf '%s
'                 "Instala o Vitals em Linux — o comando de terminal."                 ""                 "  ./install.sh                 instala ou actualiza, so para este utilizador"                 "  ./install.sh --sistema       instala em /usr/local, para todos (pede sudo)"                 "  ./install.sh --versao v1.1   instala uma versao determinada"                 "  ./install.sh --desinstalar   remove"
            exit 0 ;;
        *)              erro "argumento desconhecido: $1" ;;
    esac
    shift
done

# ---------- onde vai ficar ----------

if [ "$sistema" -eq 1 ]; then
    pasta="/usr/local/lib/vitals"
    binario="/usr/local/bin/vitals"
    if [ "$(id -u)" -ne 0 ]; then
        command -v sudo >/dev/null 2>&1 || erro "--sistema precisa de root, e não há sudo."
        SUDO="sudo"
    else
        SUDO=""
    fi
else
    pasta="${XDG_DATA_HOME:-$HOME/.local/share}/vitals"
    binario="${HOME}/.local/bin/vitals"
    SUDO=""
fi

executar() { if [ -n "$SUDO" ]; then $SUDO "$@"; else "$@"; fi; }

printf '\n%sVitals — instalação%s\n\n' "$BRANCO" "$FIM"

# ---------- desinstalar ----------

if [ "$desinstalar" -eq 1 ]; then
    removido=0
    [ -e "$binario" ] && { executar rm -f "$binario"; feito "removido $binario"; removido=1; }
    [ -d "$pasta" ]   && { executar rm -rf "$pasta";  feito "removida $pasta";  removido=1; }
    [ "$removido" -eq 0 ] && aviso "não estava instalado em $pasta."
    printf '\n'
    exit 0
fi

# ---------- arquitectura ----------

case "$(uname -s)" in
    Linux) ;;
    *) erro "este instalador é para Linux. Em Windows corre o install.ps1." ;;
esac

case "$(uname -m)" in
    x86_64|amd64)  alvo="linux-x64" ;;
    aarch64|arm64) alvo="linux-arm64" ;;
    *) erro "arquitectura não suportada: $(uname -m). Compila a partir do fonte — ver o README." ;;
esac

# ---------- que versão ----------

descarregar() {                                   # url destino
    if command -v curl >/dev/null 2>&1; then
        curl -fsSL "$1" -o "$2"
    elif command -v wget >/dev/null 2>&1; then
        wget -qO "$2" "$1"
    else
        erro "preciso do curl ou do wget."
    fi
}

ler_url() {                                       # url  → escreve no stdout
    if command -v curl >/dev/null 2>&1; then
        curl -fsSL "$1"
    else
        wget -qO- "$1"
    fi
}

if [ -z "$versao" ]; then
    passo "a procurar a última versão..."
    versao=$(ler_url "https://api.github.com/repos/$REPO/releases/latest" \
             | sed -n 's/.*"tag_name"[ ]*:[ ]*"\([^"]*\)".*/\1/p' | head -1)
    [ -n "$versao" ] || erro "não consegui descobrir a última versão. Indica-a com --versao."
fi

feito "versão $versao, $alvo"

# ---------- descarregar ----------

temporaria=$(mktemp -d)
# shellcheck disable=SC2064
trap "rm -rf '$temporaria'" EXIT INT TERM

pacote="vitals-$alvo.tar.gz"
# VITALS_URL_BASE existe para ensaiar o instalador sem publicar nada
base="${VITALS_URL_BASE:-https://github.com/$REPO/releases/download/$versao}"

passo "a descarregar $pacote..."
descarregar "$base/$pacote" "$temporaria/$pacote" \
    || erro "falhou a descarga de $base/$pacote"

# ---------- conferir ----------

if command -v sha256sum >/dev/null 2>&1 && descarregar "$base/SHA256SUMS" "$temporaria/SHA256SUMS" 2>/dev/null; then
    # o nome pode vir com o "*" de modo binario a frente
    esperado=$(awk -v f="$pacote" '{ n = $2; sub(/^\*/, "", n); if (n == f) print $1 }' "$temporaria/SHA256SUMS")
    obtido=$(sha256sum "$temporaria/$pacote" | awk '{print $1}')
    if [ -z "$esperado" ]; then
        aviso "o SHA256SUMS publicado não tem entrada para $pacote — a seguir sem verificação."
    elif [ "$esperado" != "$obtido" ]; then
        erro "o ficheiro descarregado não corresponde ao SHA256 publicado. Instalação abortada."
    else
        feito "SHA256 confere"
    fi
else
    aviso "sem SHA256SUMS para conferir — a seguir sem verificação."
fi

# ---------- instalar ----------

passo "a instalar em $pasta..."
executar rm -rf "$pasta"
executar mkdir -p "$pasta"
executar tar -xzf "$temporaria/$pacote" -C "$pasta"

# o tar pode trazer tudo dentro de uma pasta — se assim for, subir um nível
if [ ! -f "$pasta/vitals" ]; then
    interior=$(find "$pasta" -mindepth 2 -maxdepth 3 -name vitals -type f | head -1)
    [ -n "$interior" ] || erro "o pacote não trazia o executável."
    origem=$(dirname "$interior")
    executar find "$origem" -mindepth 1 -maxdepth 1 -exec mv {} "$pasta"/ ';'
fi

executar chmod +x "$pasta/vitals"
executar mkdir -p "$(dirname "$binario")"
executar ln -sf "$pasta/vitals" "$binario"

# ---------- confirmar ----------

if ! "$pasta/vitals" --ajuda >/dev/null 2>&1; then
    aviso "instalado, mas o executável não correu. Falta alguma biblioteca do sistema?"
    aviso "no Fedora: sudo dnf install libicu"
fi

feito "instalado: $binario"

# ---------- avisos finais ----------

case ":${PATH}:" in
    *":$(dirname "$binario"):"*) ;;
    *)
        printf '\n'
        aviso "$(dirname "$binario") não está no PATH. Acrescenta ao teu ~/.bashrc:"
        printf '      export PATH="%s:$PATH"\n' "$(dirname "$binario")"
        ;;
esac

if [ ! -d /sys/class/hwmon ] || [ -z "$(ls -A /sys/class/hwmon 2>/dev/null)" ]; then
    printf '\n'
    aviso "não vejo sensores em /sys/class/hwmon. Instala o lm_sensors e corre:"
    printf '      sudo sensors-detect --auto\n'
fi

printf '\n  Experimenta:  %svitals --watch%s\n\n' "$BRANCO" "$FIM"
