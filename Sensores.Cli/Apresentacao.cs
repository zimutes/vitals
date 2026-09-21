using System.Runtime.InteropServices;
using System.Text;
using Sensores.Core;

namespace Sensores.Cli;

/// <summary>
/// Desenha a leitura no terminal.
///
/// O fotograma é construído inteiro numa string e escrito de uma só vez, com sequências
/// ANSI para posicionar o cursor. Escrever linha a linha com <c>SetCursorPosition</c>
/// duplicava o desenho quando a janela era mais baixa que o conteúdo (o buffer rolava) e
/// deixava cores dessincronizadas a meio.
/// </summary>
public static class Apresentacao
{
    private const int LarguraValor = 13;
    private const int LarguraBarra = 14;

    // em Linux as etiquetas são pontos de montagem ("/srv/backup espaço") e não cabem numa
    // largura fixa, por isso a coluna mede-se a cada desenho
    private static int _larguraRotulo = 11;
    private static int LarguraLinha => _larguraRotulo + LarguraValor + LarguraBarra + 6;

    // barras com traço horizontal em vez de bloco cheio: blocos cheios em linhas seguidas
    // colam-se uns aos outros e formam uma parede; o traço fica ao centro e deixa ar.
    private const char Cheio = '━';
    private const char Meio = '╸';
    private const char Vazio = '─';

    // cores ANSI
    private const string Fim = "\x1b[0m";
    private const string Apagado = "\x1b[90m";
    private const string Branco = "\x1b[97m";
    private const string Verde = "\x1b[92m";
    private const string Amarelo = "\x1b[93m";
    private const string Vermelho = "\x1b[91m";
    private const string Ciano = "\x1b[96m";
    private const string Titulo = "\x1b[36m";

    private static bool _cor = true;

    private sealed record Medida(string Texto, float Fraccao, string Cor);

    /// <summary>Liga o modo ANSI no terminal do Windows. Sem isto, o cmd.exe clássico
    /// mostrava os códigos em vez das cores.</summary>
    public static void Preparar()
    {
        _cor = !Console.IsOutputRedirected;

        // em Linux e macOS o terminal já entende ANSI; só o Windows é que precisa de licença.
        // Antes isto corria em todo o lado, a chamada à kernel32 rebentava em Linux, e o
        // catch concluía "não há cores" — ficava sem ecrã alternativo e sem cor nenhuma.
        if (!_cor || !OperatingSystem.IsWindows()) return;

        try
        {
            var consola = GetStdHandle(-11);
            if (GetConsoleMode(consola, out var modo))
                SetConsoleMode(consola, modo | 0x0004);   // ENABLE_VIRTUAL_TERMINAL_PROCESSING
        }
        catch (Exception)
        {
            _cor = false;
        }
    }

    public static void Desenhar(Leitura l, bool limpar)
    {
        _larguraRotulo = Math.Clamp(
            l.Discos.Select(d => d.Etiqueta.Length + 7).DefaultIfEmpty(11).Max(), 11, 22);

        var quadro = new StringBuilder();
        if (limpar && _cor) quadro.Append("\x1b[H");   // cursor para o canto superior esquerdo

        Cabecalho(quadro, $"{Marca.Nome} {Marca.Versao}", Marca.Autor);

        // numa máquina sem gráfica a secção inteira desaparece, em vez de quatro "n/d"
        var temGrafica = l.GpuCore is not null || l.GpuJuncao is not null ||
                         l.GpuUso is not null || l.GpuVramTotal is not null;

        if (temGrafica)
        {
            Seccao(quadro, Curto(l.GpuNome));
            var limite = l.GpuLimite ?? 85f;
            Linha(quadro, "core", Temperatura(l.GpuCore, limite - 15, limite - 5));
            if (l.GpuHotspot is not null)
                Linha(quadro, "hotspot", Temperatura(l.GpuHotspot, limite - 15, limite - 5));
            if (l.GpuJuncao is not null)
                Linha(quadro, "junção", Temperatura(l.GpuJuncao, 85, 95, 110));
            // prefere hotspot menos core; sem hotspot, junção menos core
            if ((l.GpuDeltaHotspot ?? l.GpuDelta) is { } delta) Linha(quadro, "delta", Delta(delta));
            if (l.GpuMargem is not null) Linha(quadro, "margem", Margem(l.GpuMargem));
            if (l.GpuUso is not null) Linha(quadro, "uso", Carga(l.GpuUso));
            if (l.GpuVramTotal is not null)
                Linha(quadro, "vram", Espaco(l.GpuVramUsada / 1024f, l.GpuVramTotal / 1024f, l.GpuVramUso, 90, 98));
        }

        Seccao(quadro, Curto(l.CpuNome));
        Linha(quadro, "uso", Carga(l.CpuUso));
        if (l.CpuTemp is not null) Linha(quadro, "temp", Temperatura(l.CpuTemp, 75, 85));

        Seccao(quadro, "memória");
        Linha(quadro, "ram", Espaco(l.RamUsada, l.RamTotal, l.RamUso, 85, 95));

        if (l.Bateria is not null)
        {
            Seccao(quadro, "bateria");
            Linha(quadro, l.BateriaACarregar == true ? "a carregar" : "carga",
                Carga(l.Bateria));
        }

        if (l.Discos.Count > 0)
        {
            Seccao(quadro, "discos");
            foreach (var disco in l.Discos)
            {
                if (disco.TemSensorProprio)
                    Linha(quadro, $"{disco.Etiqueta} temp",
                        Temperatura(disco.Temperatura, disco.LimiteAviso ?? 55, disco.LimiteCritico ?? 70, 90));
                Linha(quadro, $"{disco.Etiqueta} espaço", EspacoDisco(disco));
            }
        }

        Rodape(quadro, $"ligado há {TempoLigado()}", DateTime.Now.ToString("HH:mm:ss"));

        Console.Out.Write(limpar && _cor ? "\x1b[H" + Coube(quadro.ToString()) + "\x1b[J" : quadro.ToString());
        Console.Out.Flush();
    }

    /// <summary>Quantas linhas o painel está deslocado, quando não cabe todo na janela.</summary>
    public static int Deslocamento { get; private set; }

    /// <summary>Verdadeiro quando há mais painel do que janela, e portanto vale a pena rolar.</summary>
    public static bool Rolavel { get; private set; }

    private static int _maximoDeslocamento;

    /// <summary>Move o painel. Devolve verdadeiro se alguma coisa mudou.</summary>
    public static bool Rolar(int linhas)
    {
        var novo = Math.Clamp(Deslocamento + linhas, 0, _maximoDeslocamento);
        if (novo == Deslocamento) return false;
        Deslocamento = novo;
        return true;
    }

    public static bool RolarParaOTopo() => Rolar(-int.MaxValue / 2);
    public static bool RolarParaOFim() => Rolar(int.MaxValue / 2);

    /// <summary>
    /// Ajusta o desenho à janela e tira a mudança de linha final.
    ///
    /// Sem isto, um painel mais alto que o terminal fazia-o rolar a cada fotograma, e o
    /// desenho anterior ficava para trás. Quando não cabe, mostra-se a parte escolhida e
    /// uma última linha a dizer onde se está e como se anda.
    /// </summary>
    private static string Coube(string texto)
    {
        var linhas = texto.TrimEnd('\n').Split('\n');

        var altura = AlturaDaJanela();
        if (altura is null || linhas.Length <= altura - 1)
        {
            Rolavel = false;
            Deslocamento = 0;
            _maximoDeslocamento = 0;
            return string.Join('\n', linhas);
        }

        var cabem = altura.Value - 2;                  // uma linha fica para a barra de estado
        _maximoDeslocamento = Math.Max(0, linhas.Length - cabem);
        Deslocamento = Math.Clamp(Deslocamento, 0, _maximoDeslocamento);
        Rolavel = true;

        var visiveis = linhas.Skip(Deslocamento).Take(cabem).ToList();
        visiveis.Add(BarraDeEstado(Deslocamento + 1, Math.Min(Deslocamento + cabem, linhas.Length), linhas.Length));

        return string.Join('\n', visiveis);
    }

    private static string BarraDeEstado(int primeira, int ultima, int total)
    {
        var posicao = $"  linhas {primeira}–{ultima} de {total}";
        var ajuda = "↑↓ rolar · home/end · q sair  ";
        var espaco = Math.Max(1, LarguraLinha - posicao.Length - ajuda.Length);

        return Pinta(Titulo, posicao) + new string(' ', espaco) + Pinta(Apagado, ajuda);
    }

    /// <summary>
    /// Altura da janela, ou null quando não se sabe. Por SSH, e em terminais que não
    /// anunciam o tamanho, isto vem a zero ou dispara excepção; tomar esses casos por uma
    /// janela minúscula fazia o painel desaparecer todo atrás de "aumenta a janela".
    /// </summary>
    private static int? AlturaDaJanela()
    {
        if (Console.IsOutputRedirected) return null;

        try
        {
            var altura = Console.WindowHeight;
            return altura >= 8 ? altura : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Ecrã alternativo, como o htop: o painel não fica no histórico do terminal.</summary>
    public static void AbrirEcra()
    {
        if (_cor) Console.Out.Write("\x1b[?1049h\x1b[?25l\x1b[2J");
    }

    public static void FecharEcra()
    {
        if (_cor) Console.Out.Write("\x1b[?25h\x1b[?1049l");
        Console.Out.Flush();
    }

    // ---------- peças ----------

    private static void Cabecalho(StringBuilder q, string esquerda, string direita)
    {
        var espaco = Math.Max(1, LarguraLinha - esquerda.Length - direita.Length - 4);
        q.Append("  ").Append(Pinta(Branco, esquerda))
         .Append(new string(' ', espaco))
         .Append(Pinta(Apagado, direita)).Append("  ");
        Fecha(q);
    }

    private static void Seccao(StringBuilder q, string titulo)
    {
        Fecha(q, vazia: true);
        q.Append("  ").Append(Pinta(Titulo, titulo.ToUpperInvariant()));
        Fecha(q, preenchida: 2 + titulo.Length);
    }

    private static void Linha(StringBuilder q, string rotulo, Medida? medida)
    {
        q.Append("    ").Append(Pinta(Apagado, Cortar(rotulo, _larguraRotulo).PadRight(_larguraRotulo)));

        if (medida is null)
        {
            q.Append(Pinta(Apagado, "n/d".PadLeft(LarguraValor))).Append("  ")
             .Append(Pinta(Apagado, new string(Vazio, LarguraBarra)));
            Fecha(q, preenchida: LarguraLinha);
            return;
        }

        var corDoTexto = medida.Cor == Verde || medida.Cor == Ciano ? Branco : medida.Cor;
        q.Append(Pinta(corDoTexto, medida.Texto.PadLeft(LarguraValor))).Append("  ")
         .Append(Barra(medida.Fraccao, medida.Cor));
        Fecha(q, preenchida: LarguraLinha);
    }

    private static void Rodape(StringBuilder q, string esquerda, string direita)
    {
        Fecha(q, vazia: true);
        var espaco = Math.Max(1, LarguraLinha - esquerda.Length - direita.Length - 4);
        q.Append("  ").Append(Pinta(Apagado, esquerda + new string(' ', espaco) + direita)).Append("  ");
        Fecha(q, preenchida: LarguraLinha);
    }

    /// <summary>Termina a linha, apagando restos do fotograma anterior.</summary>
    private static void Fecha(StringBuilder q, bool vazia = false, int preenchida = 0)
    {
        if (vazia) q.Append(new string(' ', LarguraLinha));
        else if (preenchida > 0 && preenchida < LarguraLinha) q.Append(new string(' ', LarguraLinha - preenchida));

        if (_cor) q.Append("\x1b[K");   // apagar ate ao fim da linha
        q.Append('\n');
    }

    /// <summary>Barra com resolucao de meio caractere: "━━━━━━━╸──────".</summary>
    private static string Barra(float fraccao, string cor)
    {
        var total = Math.Clamp(fraccao, 0, 1) * LarguraBarra;
        var cheios = (int)total;
        var meio = cheios < LarguraBarra && total - cheios >= 0.35f;

        var preenchido = new string(Cheio, cheios) + (meio ? Meio.ToString() : "");
        var resto = new string(Vazio, Math.Max(0, LarguraBarra - preenchido.Length));

        return Pinta(cor, preenchido) + Pinta(Apagado, resto);
    }

    private static string Pinta(string cor, string texto) => _cor ? cor + texto + Fim : texto;

    // ---------- valores ----------

    private static Medida? Temperatura(float? valor, float aviso, float alerta, float escala = 100)
    {
        if (valor is not { } v) return null;
        return new Medida($"{v:F0} °C", v / escala, v >= alerta ? Vermelho : v >= aviso ? Amarelo : Verde);
    }

    private static Medida? Margem(float? margem)
    {
        if (margem is not { } m) return null;
        return new Medida($"{m:F0} °C", m / 40f, m <= 5 ? Vermelho : m <= 15 ? Amarelo : Verde);
    }

    /// <summary>Delta junção−core: aqui o que conta é a diferença, não o valor absoluto.</summary>
    private static Medida? Delta(float? delta)
    {
        if (delta is not { } d) return null;
        return new Medida($"+{d:F0} °C", d / 40f, d >= 25 ? Vermelho : d >= 15 ? Amarelo : Verde);
    }

    private static Medida? Carga(float? valor) =>
        valor is { } v ? new Medida($"{v:F0} %", v / 100f, Ciano) : null;

    private static Medida? Espaco(float? usado, float? total, float? percentagem, float aviso, float alerta)
    {
        if (usado is not { } u || total is not { } t || percentagem is not { } p) return null;
        return new Medida($"{u:F1}/{t:F1} GB", p / 100f, p >= alerta ? Vermelho : p >= aviso ? Amarelo : Ciano);
    }

    private static Medida? EspacoDisco(Disco disco)
    {
        if (disco.UsadoGB is not { } usado || disco.TotalGB is not { } total || disco.UsoEspaco is not { } p)
            return null;
        return new Medida($"{usado:F0}/{total:F0} GB", p / 100f, p >= 95 ? Vermelho : p >= 85 ? Amarelo : Ciano);
    }

    // ---------- texto ----------

    private static string TempoLigado()
    {
        var tempo = TimeSpan.FromMilliseconds(Environment.TickCount64);
        if (tempo.TotalDays >= 1) return $"{(int)tempo.TotalDays}d {tempo.Hours}h{tempo.Minutes:00}";
        if (tempo.TotalHours >= 1) return $"{(int)tempo.TotalHours}h{tempo.Minutes:00}";
        return $"{tempo.Minutes}m";
    }

    private static string Curto(string nome) => nome
        .Replace("NVIDIA GeForce ", "")
        .Replace(" Processor", "")
        .Trim();

    private static string Cortar(string texto, int limite) =>
        texto.Length <= limite ? texto : texto[..limite];

    /// <summary>Quantas linhas ocupa o desenho, para avisar se a janela for pequena.</summary>
    public static int Altura(Leitura l) =>
        1
        + (l.GpuCore is null && l.GpuUso is null && l.GpuVramTotal is null
            ? 0
            : 2 + 2 + (l.GpuJuncao is null ? 0 : 1) + (l.GpuMargem is null ? 0 : 1)
                + (l.GpuDelta is null ? 0 : 1) + (l.GpuHotspot is null ? 0 : 1))
        + 2 + 1 + (l.CpuTemp is null ? 0 : 1)
        + 2 + 1
        + (l.Bateria is null ? 0 : 3)
        + (l.Discos.Count > 0 ? 2 + l.Discos.Count + l.Discos.Count(d => d.TemSensorProprio) : 0)
        + 2;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int identificador);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr consola, out uint modo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr consola, uint modo);
}
