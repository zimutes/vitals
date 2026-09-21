using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Sensores.Core;
// o WinForms (preciso para o ícone junto às horas) traz nomes que chocam com os do WPF
using Path = System.IO.Path;
using Panel = System.Windows.Controls.Panel;
using Rectangle = System.Windows.Shapes.Rectangle;
using ToolTip = System.Windows.Controls.ToolTip;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using Clipboard = System.Windows.Clipboard;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;

namespace Sensores.Widget;

public partial class MainWindow : Window
{
    private const double LarguraBarra = 54;
    private const double LarguraRotulo = 64;
    private const double LarguraUmaColuna = 228;
    private const double LarguraDuasColunas = 444;
    private const double AlturaLinha = 21;
    private const double AlturaCabecalho = 24;
    private const string ChaveArranque = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static readonly SolidColorBrush Normal = Congelado("#F2F2F4");
    private static readonly SolidColorBrush Acento = Congelado("#7DD3FC");
    private static readonly SolidColorBrush Fresco = Congelado("#86EFAC");
    private static readonly SolidColorBrush Aviso = Congelado("#FBBF24");
    private static readonly SolidColorBrush Alerta = Congelado("#F87171");
    private static readonly SolidColorBrush Fraco = Congelado("#7A7A85");
    private static readonly SolidColorBrush Legenda = Congelado("#C3C3D0");

    private static readonly string FicheiroConfig = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Marca.Nome, "config.json");

    /// <summary>Um valor pronto a mostrar: texto, quanto enche a barra, e com que cor.</summary>
    private sealed record Medida(string Texto, float Fraccao, SolidColorBrush Cor, bool Destacar = false);

    /// <summary>Uma linha possível do widget. O Id é o que fica guardado nas preferências;
    /// a Descrição é o que se vê ao passar o rato e na janela de explicação.</summary>
    private sealed record Metrica(string Id, string Grupo, string Rotulo, string Descricao, Func<Leitura, Medida?> Ler);

    private sealed record Config
    {
        public double? Esquerda { get; init; }
        public double? Topo { get; init; }
        public bool SempreAFrente { get; init; } = true;
        public double Intervalo { get; init; } = 1;
        public string[]? Visiveis { get; init; }
        public bool JaConfigurado { get; init; }

        /// <summary>Só existem depois de a janela ser redimensionada à mão.</summary>
        public double? Largura { get; init; }
        public double? Altura { get; init; }
        public string? Densidade { get; init; }
    }

    private readonly Leitor _leitor = new();
    private readonly DispatcherTimer _relogio = new();
    private readonly Dictionary<string, (TextBlock Valor, Rectangle Barra)> _linhas = new();

    private System.Windows.Forms.NotifyIcon? _bandeja;
    private List<Metrica> _metricas = new();
    private HashSet<string>? _visiveis;
    private string _disposicao = "";
    private double _intervalo = 1;
    private Leitura? _ultima;
    private bool _aLer;
    private bool _menuPronto;
    private bool _aSairMesmo;
    private bool _migrado;
    private bool _compacto;
    private int _escondidas;
    private bool _aAplicar;
    private string _densidade = "auto";   // auto | compacta | normal
    private bool _aRedimensionar;
    private Point _origemRato;
    private Size _tamanhoInicial;

    public MainWindow()
    {
        InitializeComponent();

        txtNome.Text = Marca.Nome.ToUpperInvariant();
        txtAutor.Text = Marca.Autor;
        txtRodape.Text = "a ler sensores...";

        var config = CarregarConfig();
        Topmost = config.SempreAFrente;
        menuTopo.IsChecked = config.SempreAFrente;
        _intervalo = Math.Clamp(config.Intervalo, 0.5, 60);
        _densidade = config.Densidade is "auto" or "compacta" or "normal" ? config.Densidade : "auto";
        _visiveis = config.Visiveis is { Length: > 0 } guardadas ? new HashSet<string>(guardadas) : null;

        // tamanho escolhido à mão numa sessão anterior
        if (config.Largura is { } largura && config.Altura is { } altura && largura >= MinWidth && altura >= MinHeight)
        {
            SizeToContent = SizeToContent.Manual;
            Width = largura;
            Height = altura;
        }

        ConfigurarBandeja();

        // primeira execução: fica a arrancar com o Windows (desliga-se no menu)
        if (!config.JaConfigurado && !ArranqueActivo()) DefinirArranque(true);
        menuArranque.IsChecked = ArranqueActivo();

        _relogio.Interval = TimeSpan.FromSeconds(_intervalo);
        _relogio.Tick += async (_, _) => await Actualizar();

        SourceInitialized += (_, _) => Posicionar(config);
        SizeChanged += (_, _) =>
        {
            TrazerParaDentro();
            // ao encolher, recalcular quantas linhas cabem (sem entrar em ciclo)
            if (!_aAplicar && SizeToContent == SizeToContent.Manual && _ultima is { } leitura)
            {
                _aAplicar = true;
                try { Aplicar(leitura); } finally { _aAplicar = false; }
            }
        };
        Loaded += async (_, _) => { await Actualizar(); _relogio.Start(); };
    }

    // ---------- ciclo de leitura ----------

    private async Task Actualizar()
    {
        if (_aLer) return;
        _aLer = true;
        try
        {
            var leitura = await Task.Run(() => _leitor.Ler());
            _ultima = leitura;
            Aplicar(leitura);
        }
        catch (Exception)
        {
            // uma leitura falhada nao deve matar o widget: fica o valor anterior e tenta outra vez
        }
        finally
        {
            _aLer = false;
        }
    }

    private void Aplicar(Leitura l)
    {
        _metricas = Metricas(l).ToList();
        _visiveis ??= PorOmissao(_metricas);

        if (!_migrado) { MigrarDiscos(); _migrado = true; }

        if (!_menuPronto) { ConstruirMenus(); _menuPronto = true; }

        var visiveis = _metricas.Where(m => _visiveis.Contains(m.Id)).ToList();

        _compacto = DeveSerCompacto(visiveis.Count);
        var escondidas = 0;

        if (_compacto)
        {
            // o que é crítico fica em cima, e o que não couber é cortado em vez de rolar:
            // quem encolhe a janela quer os números à frente, não uma lista para percorrer
            visiveis = visiveis.OrderBy(Prioridade).ToList();

            var cabem = QuantasCabem();
            if (visiveis.Count > cabem)
            {
                escondidas = visiveis.Count - cabem;
                visiveis = visiveis.Take(cabem).ToList();
            }
        }

        _escondidas = escondidas;

        // o modo entra na chave para que mudar o tamanho à mão refaça a disposição
        var modo = SizeToContent == SizeToContent.Manual ? $"col{QuantasColunas()}" : "auto";
        var disposicao = $"{modo}|{(_compacto ? "denso" : "folgado")}|" + string.Join("|", visiveis.Select(m => m.Id));
        if (disposicao != _disposicao)
        {
            ReconstruirLinhas(visiveis);
            _disposicao = disposicao;
        }

        foreach (var metrica in visiveis)
        {
            if (!_linhas.TryGetValue(metrica.Id, out var linha)) continue;
            var medida = metrica.Ler(l);

            if (medida is null)
            {
                linha.Valor.Text = "n/d";
                linha.Valor.Foreground = Fraco;
                linha.Barra.Width = 0;
                continue;
            }

            linha.Valor.Text = medida.Texto;
            linha.Valor.Foreground = medida.Destacar ? medida.Cor : Normal;
            linha.Barra.Fill = medida.Cor;
            linha.Barra.Width = Math.Clamp(medida.Fraccao, 0, 1) * LarguraDaBarraActual();
        }

        txtRodape.Text = $"ligado há {TempoLigado()} · {DateTime.Now:HH:mm:ss} · {Intervalo(_intervalo)}";
        pulso.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(700)));

        if (_bandeja is not null) _bandeja.Text = TextoDaBandeja(l);
    }

    /// <summary>Há quanto tempo o Windows arrancou.</summary>
    private static string TempoLigado()
    {
        var tempo = TimeSpan.FromMilliseconds(Environment.TickCount64);
        if (tempo.TotalDays >= 1) return $"{(int)tempo.TotalDays}d {tempo.Hours}h{tempo.Minutes:00}";
        if (tempo.TotalHours >= 1) return $"{(int)tempo.TotalHours}h{tempo.Minutes:00}";
        return $"{tempo.Minutes}m";
    }

    // ---------- métricas disponíveis ----------

    private static IEnumerable<Metrica> Metricas(Leitura l)
    {
        var limite = l.GpuLimite ?? 85f;   // limiares vindos da própria placa

        yield return new("gpu.core", "GPU", "core",
            "Temperatura do chip da gráfica, no sensor principal. É a mesma que o Gestor de Tarefas mostra.",
            x => Temperatura(x.GpuCore, limite - 15, limite - 5));

        yield return new("gpu.juncao", "GPU", "junção",
            "Temperatura de junção da memória GDDR7: o ponto mais quente da VRAM. É o valor que na AMD se chama junction e o mais próximo de um \"hotspot\" que esta placa expõe.",
            x => Temperatura(x.GpuJuncao, 85, 95, 110));

        if (l.GpuHotspot is not null)
            yield return new("gpu.hotspot", "GPU", "hotspot",
                "Ponto mais quente do chip. A NVIDIA não o expõe em API nenhuma nas GeForce recentes: " +
                "este valor vem do HWiNFO, que o lê com driver próprio e o publica no registo do Windows. " +
                "Se o HWiNFO fechar, a linha desaparece.",
                x => Temperatura(x.GpuHotspot, limite - 15, limite - 5));

        yield return new("gpu.delta", "GPU", "delta",
            "Diferença entre a junção e o core — o \"delta hotspot\" dos fóruns. Mede a qualidade da " +
            "transferência de calor: quanto maior, pior está a pasta ou os pads. Até 15 °C é normal, " +
            "acima de 25 °C costuma indicar problema. Usa o hotspot quando o HWiNFO o estiver a " +
            "publicar; caso contrário usa a junção da memória, que é o que a NVIDIA expõe.",
            x => Delta(x.GpuDeltaHotspot ?? x.GpuDelta));

        yield return new("gpu.margem", "GPU", "margem",
            $"Graus que faltam até a placa começar a reduzir desempenho. O limite de operação é {limite:F0} °C, " +
            "e este número é a subtracção. Abaixo de 15 fica âmbar, abaixo de 5 fica vermelho.",
            x => MargemTermica(x.GpuMargem));

        yield return new("gpu.uso", "GPU", "uso",
            "Percentagem de tempo em que a gráfica está a trabalhar. 100 % durante um render ou um jogo é normal.",
            x => Carga(x.GpuUso));

        yield return new("gpu.vram", "GPU", "vram",
            "Memória da gráfica em uso. Quando enche, o Blender passa a usar memória do sistema e o render fica muito mais lento.",
            x => Espaco(x.GpuVramUsada / 1024f, x.GpuVramTotal / 1024f, x.GpuVramUso, 90, 98));

        yield return new("gpu.potencia", "GPU", "potência",
            "Consumo da placa em watts, no conector e no slot somados.",
            x => Escalar(x.GpuPotencia, "W", 300));

        yield return new("gpu.ventoinha", "GPU", "ventoinha",
            "Rotação da ventoinha da gráfica. A zero com a placa fria é normal: pára em repouso.",
            x => Escalar(x.GpuVentoinha, "rpm", 3000));

        yield return new("cpu.uso", "CPU", "uso",
            "Carga total do processador, com os 16 fios somados. Um núcleo saturado sozinho dá cerca de 6 %.",
            x => Carga(x.CpuUso));

        if (l.CpuTemp is not null)
            yield return new("cpu.temp", "CPU", "temp",
                "Temperatura do processador (Tctl/Tdie).",
                x => Temperatura(x.CpuTemp, 75, 85));

        yield return new("ram", "MEMÓRIA", "ram",
            "Memória do sistema em uso, sem contar com o ficheiro de paginação.",
            x => Espaco(x.RamUsada, x.RamTotal, x.RamUso, 85, 95));

        if (l.Bateria is not null)
            yield return new("bateria", "BATERIA", l.BateriaACarregar == true ? "a carregar" : "carga",
                "Carga da bateria, lida ao Windows. Só aparece em portáteis.",
                x => x.Bateria is { } carga
                    ? new Medida($"{carga:F0} %", carga / 100f,
                        carga <= 10 ? Alerta : carga <= 25 ? Aviso : Fresco, carga <= 25)
                    : null);

        foreach (var disco in l.Discos)
        {
            var id = disco.Id;
            var etiqueta = disco.Etiqueta;
            var modelo = disco.Modelo;

            // os NVMe publicam os próprios limites; os discos rígidos não, e aí vale o conservador
            var aviso = disco.LimiteAviso ?? 55;
            var critico = disco.LimiteCritico ?? 70;

            // a temperatura é do disco físico: nas outras partições seria o mesmo número repetido
            if (disco.TemSensorProprio)
            yield return new($"disco{id}.temp", "DISCOS", $"{etiqueta} temp",
                $"Temperatura de {etiqueta} ({modelo}), lida pelo SMART. " +
                (disco.LimiteAviso is null
                    ? "Âmbar aos 55 °C, vermelho aos 70 (limiares conservadores: este disco não publica os dele)."
                    : $"Âmbar aos {aviso:F0} °C e vermelho aos {critico:F0}, que são os limites publicados pelo próprio disco.") +
                " Aparece \"n/d\" se o programa não correr como administrador: o SMART exige-o.",
                x => Temperatura(Procurar(x, id)?.Temperatura, aviso, critico, critico + 10));

            yield return new($"disco{id}.espaco", "DISCOS", $"{etiqueta} espaço",
                $"Espaço ocupado em {etiqueta}. Âmbar acima de 85 %, vermelho acima de 95 % — um disco de sistema cheio trava o Windows. " +
                "Este valor não precisa de privilégios.",
                x => EspacoDisco(Procurar(x, id)));

            yield return new($"disco{id}.uso", "DISCOS", $"{etiqueta} uso",
                $"Actividade de {etiqueta}: percentagem de tempo a ler ou a escrever. 100 % constante indica que o disco é o travão. " +
                "Também precisa de administrador.",
                x => Carga(Procurar(x, id)?.Actividade));
        }
    }

    private static Disco? Procurar(Leitura l, string id) => l.Discos.FirstOrDefault(d => d.Id == id);

    /// <summary>
    /// Pequeno quer dizer "estou a jogar": nesse caso o widget aperta tudo, larga cabeçalho,
    /// rodapé e títulos de secção, e põe em cima o que é crítico. Grande pode respirar.
    /// </summary>
    private bool DeveSerCompacto(int quantasLinhas)
    {
        if (_densidade == "compacta") return true;
        if (_densidade == "normal") return false;

        if (SizeToContent != SizeToContent.Manual) return false;   // a crescer sozinho, cabe sempre
        if (ActualHeight <= 0) return false;

        // estreito também é pequeno: com pouca largura os títulos de secção e o enchimento
        // roubam espaço às legendas, que acabavam cortadas
        if (ActualWidth < 210) return true;

        // espaço que cada linha precisa em modo folgado, mais cabeçalho e rodapé
        var precisaFolgado = 56 + quantasLinhas * AlturaLinha / (ActualWidth >= 380 ? 2 : 1);
        return ActualHeight < precisaFolgado || ActualHeight < 220;
    }

    private const double AlturaLinhaDensa = 15;     // texto de 10,5 px mais meia margem
    private const double LarguraColunaDensa = 172;  // rótulo + valor + barra, sem apertar
    private const double LarguraColunaFolgada = 230;

    /// <summary>
    /// Quantas colunas cabem à largura actual. Baixo e largo passa a encher-se de lado:
    /// uma tira fina com quatro colunas mostra tanto como um quadrado alto.
    /// </summary>
    private int QuantasColunas()
    {
        if (SizeToContent != SizeToContent.Manual) return ActualWidth >= 380 ? 2 : 1;

        var util = Math.Max(0, ActualWidth - 16);
        return Math.Clamp((int)(util / LarguraColunaDensa), 1, 4);
    }

    /// <summary>Largura da barra para a janela actual — zero quando não há espaço para ela.</summary>
    private double LarguraDaBarraActual()
    {
        if (SizeToContent != SizeToContent.Manual) return LarguraBarra;
        if (ActualWidth < 190) return 0;
        return _compacto && ActualWidth < 260 ? 34 : LarguraBarra;
    }

    /// <summary>Quantas linhas cabem, contando a altura e as colunas.</summary>
    private int QuantasCabem()
    {
        if (SizeToContent != SizeToContent.Manual || ActualHeight <= 0) return int.MaxValue;

        var util = ActualHeight - 12;                // moldura e enchimento
        var porColuna = Math.Max(1, (int)(util / AlturaLinhaDensa));

        return porColuna * QuantasColunas();
    }

    /// <summary>Ordem de importância quando o espaço é pouco: temperaturas primeiro.</summary>
    private static int Prioridade(Metrica m) => m.Id switch
    {
        "gpu.core" => 0,
        "gpu.hotspot" => 1,
        // o delta vem antes da junção: um número sozinho diz menos que a diferença entre dois
        "gpu.delta" => 2,
        "gpu.margem" => 3,
        "gpu.juncao" => 4,
        "cpu.temp" => 5,
        "gpu.uso" => 6,
        "cpu.uso" => 7,
        "gpu.vram" => 8,
        "ram" => 9,
        "bateria" => 10,
        var id when id.EndsWith(".temp") => 11,
        _ => 20,
    };

    /// <summary>
    /// As linhas de disco passaram a ser identificadas pela letra do volume ("C:") em vez do
    /// caminho interno da biblioteca ("/nvme/2"). Quem tinha configuração antiga ficaria com
    /// linhas a apontar para o nada, por isso trocam-se pelas equivalentes de hoje.
    /// </summary>
    private void MigrarDiscos()
    {
        var conhecidos = _metricas.Select(m => m.Id).ToHashSet();
        var orfaos = _visiveis!.Where(id => id.StartsWith("disco") && !conhecidos.Contains(id)).ToList();
        if (orfaos.Count == 0) return;

        foreach (var id in orfaos) _visiveis.Remove(id);

        foreach (var metrica in _metricas.Where(m => m.Grupo == "DISCOS"))
            if (metrica.Id.EndsWith(".temp") || metrica.Id.EndsWith(".espaco"))
                _visiveis.Add(metrica.Id);

        GuardarConfig();
    }

    /// <summary>Primeira utilização: GPU, CPU, RAM e todos os discos com temperatura e espaço.</summary>
    private static HashSet<string> PorOmissao(IEnumerable<Metrica> metricas)
    {
        var conjunto = new HashSet<string>
        {
            "gpu.core", "gpu.juncao", "gpu.margem", "gpu.uso", "gpu.vram", "cpu.uso", "ram",
        };

        foreach (var metrica in metricas.Where(m => m.Grupo == "DISCOS"))
            if (metrica.Id.EndsWith(".temp") || metrica.Id.EndsWith(".espaco"))
                conjunto.Add(metrica.Id);

        return conjunto;
    }

    // ---------- conversão de valores em medidas ----------

    private static Medida? Temperatura(float? valor, float aviso, float alerta, float escala = 100)
    {
        if (valor is not { } v) return null;
        var cor = v >= alerta ? Alerta : v >= aviso ? Aviso : Fresco;
        return new Medida($"{v:F0} °C", v / escala, cor, v >= aviso);
    }

    /// <summary>A margem térmica lê-se ao contrário das outras: quanto menor, pior.</summary>
    private static Medida? MargemTermica(float? margem)
    {
        if (margem is not { } m) return null;
        var cor = m <= 5 ? Alerta : m <= 15 ? Aviso : Fresco;
        return new Medida($"{m:F0} °C", m / 40f, cor, m <= 15);
    }

    /// <summary>Delta junção−core: ao contrário das temperaturas, o que conta é a diferença.</summary>
    private static Medida? Delta(float? delta)
    {
        if (delta is not { } d) return null;
        var cor = d >= 25 ? Alerta : d >= 15 ? Aviso : Fresco;
        return new Medida($"+{d:F0} °C", d / 40f, cor, d >= 15);
    }

    private static Medida? Carga(float? valor) =>
        valor is { } v ? new Medida($"{v:F0} %", v / 100f, Acento) : null;

    private static Medida? Escalar(float? valor, string unidade, float escala) =>
        valor is { } v ? new Medida($"{v:F0} {unidade}", v / escala, Acento) : null;

    private static Medida? Espaco(float? usado, float? total, float? percentagem, float aviso, float alerta)
    {
        if (usado is not { } u || total is not { } t || percentagem is not { } p) return null;
        var cor = p >= alerta ? Alerta : p >= aviso ? Aviso : Acento;
        return new Medida($"{u:F1}/{t:F1} GB", p / 100f, cor, p >= aviso);
    }

    private static Medida? EspacoDisco(Disco? disco)
    {
        if (disco?.UsadoGB is not { } usado || disco.TotalGB is not { } total || disco.UsoEspaco is not { } p)
            return null;
        var cor = p >= 95 ? Alerta : p >= 85 ? Aviso : Acento;
        return new Medida($"{usado:F0}/{total:F0} GB", p / 100f, cor, p >= 85);
    }

    // ---------- construção das linhas ----------

    private void ReconstruirLinhas(List<Metrica> visiveis)
    {
        conteudo.Children.Clear();
        _linhas.Clear();
        AplicarDensidade();

        if (_compacto)
        {
            // sem títulos de secção: em modo denso cada título custa uma linha inteira
            var colunas = Math.Min(QuantasColunas(), Math.Max(1, visiveis.Count));

            if (colunas <= 1)
            {
                foreach (var m in visiveis) conteudo.Children.Add(CriarLinha(m));
            }
            else
            {
                var painéis = ColunasVazias(colunas, out var grelhaDensa);
                var porColuna = (int)Math.Ceiling(visiveis.Count / (double)colunas);

                for (var i = 0; i < visiveis.Count; i++)
                    painéis[Math.Min(i / porColuna, colunas - 1)].Children.Add(CriarLinha(visiveis[i]));

                conteudo.Children.Add(grelhaDensa);
            }

            rolagem.MaxHeight = double.PositiveInfinity;
            return;
        }

        var grupos = visiveis
            .GroupBy(m => m.Grupo)
            .Select(g => g.ToList())
            .ToList();

        var disponivel = Math.Max(200, AreaUtil().Height - 190);

        int quantasColunas;
        if (SizeToContent == SizeToContent.Manual)
        {
            // tamanho escolhido à mão: a largura manda, até quatro colunas
            quantasColunas = Math.Clamp((int)((ActualWidth - 16) / LarguraColunaFolgada), 1, 4);
            rolagem.MaxHeight = double.PositiveInfinity;   // o DockPanel já limita
        }
        else
        {
            // automático: uma coluna muito alta é desconfortável muito antes do limite do ecrã
            var confortavel = Math.Min(disponivel, 430);
            quantasColunas = grupos.Sum(Altura) > confortavel ? 2 : 1;
            Width = quantasColunas == 2 ? LarguraDuasColunas : LarguraUmaColuna;
            rolagem.MaxHeight = disponivel;
        }

        quantasColunas = Math.Min(quantasColunas, grupos.Count);

        if (quantasColunas <= 1)
        {
            foreach (var grupo in grupos) Despejar(grupo, conteudo);
            return;
        }

        // os grupos não se partem ao meio: cada um vai inteiro para a coluna mais curta
        var colunasFolgadas = ColunasVazias(quantasColunas, out var grelha);
        var alturas = new double[quantasColunas];

        foreach (var grupo in grupos)
        {
            var maisCurta = Array.IndexOf(alturas, alturas.Min());
            Despejar(grupo, colunasFolgadas[maisCurta]);
            alturas[maisCurta] += Altura(grupo);
        }

        conteudo.Children.Add(grelha);
    }

    private static double Altura(List<Metrica> grupo) => AlturaCabecalho + grupo.Count * AlturaLinha;

    /// <summary>Cria N colunas com um intervalo entre elas.</summary>
    private List<StackPanel> ColunasVazias(int quantas, out Grid grelha)
    {
        grelha = new Grid();
        var painéis = new List<StackPanel>();

        for (var i = 0; i < quantas; i++)
        {
            if (i > 0)
                grelha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_compacto ? 10 : 16) });

            grelha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var painel = new StackPanel();
            Grid.SetColumn(painel, grelha.ColumnDefinitions.Count - 1);
            grelha.Children.Add(painel);
            painéis.Add(painel);
        }

        return painéis;
    }

    private Grid DuasColunasVazias(out StackPanel esquerda, out StackPanel direita)
    {
        var grelha = new Grid();
        grelha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grelha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_compacto ? 10 : 16) });
        grelha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        esquerda = new StackPanel();
        direita = new StackPanel();
        Grid.SetColumn(esquerda, 0);
        Grid.SetColumn(direita, 2);
        grelha.Children.Add(esquerda);
        grelha.Children.Add(direita);
        return grelha;
    }

    /// <summary>Aperta ou folga a moldura, o cabeçalho e o rodapé conforme a densidade.</summary>
    private void AplicarDensidade()
    {
        moldura.Padding = _compacto ? new Thickness(7, 4, 7, 4) : new Thickness(12, 9, 12, 8);
        moldura.CornerRadius = new CornerRadius(_compacto ? 7 : 10);

        cabecalho.Visibility = _compacto ? Visibility.Collapsed : Visibility.Visible;
        rodapeCaixa.Visibility = _compacto ? Visibility.Collapsed : Visibility.Visible;

        // em modo denso a identificação passa para a dica: o espaço é para os números
        ToolTip = _compacto
            ? $"{Marca.Assinatura} — botão direito para as opções" +
              (_escondidas > 0 ? $"\n{_escondidas} linha(s) escondidas: aumenta a janela para as ver" : "")
            : null;
    }

    private void Despejar(List<Metrica> grupo, Panel destino)
    {
        destino.Children.Add(new TextBlock
        {
            Text = grupo[0].Grupo,
            Style = (Style)FindResource("Secao"),
        });

        foreach (var metrica in grupo) destino.Children.Add(CriarLinha(metrica));
    }

    private UIElement CriarLinha(Metrica metrica)
    {
        var grelha = new Grid
        {
            Margin = new Thickness(0, _compacto ? 0.5 : 4, 0, 0),
            Background = Brushes.Transparent,      // para o rato apanhar a linha toda
            ToolTip = Dica(metrica),
        };
        // numa janela estreita a barra é o primeiro a sair: o número e a legenda valem mais
        var larguraBarra = LarguraDaBarraActual();

        // o rótulo é que encolhe (com reticências); o valor leva sempre a largura de que
        // precisa, senão perdiam-se dígitos à esquerda
        grelha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 26 });
        grelha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grelha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(larguraBarra) });

        // sem títulos de secção, "uso" sozinho seria ambíguo entre GPU e CPU
        var texto = _compacto && metrica.Grupo is "GPU" or "CPU"
            ? $"{metrica.Grupo.ToLowerInvariant()} {metrica.Rotulo}"
            : metrica.Rotulo;

        var rotulo = new TextBlock { Text = texto, Style = (Style)FindResource("Rotulo") };
        var valor = new TextBlock { Style = (Style)FindResource("Valor") };

        if (_compacto)
        {
            rotulo.FontSize = 10;
            valor.FontSize = 10.5;
            valor.Margin = new Thickness(4, 0, 5, 0);
            rotulo.LineHeight = 14;
            valor.LineHeight = 14;
            rotulo.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            valor.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }

        var altura = _compacto ? 2.5 : 4.0;
        var barra = new Rectangle
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Height = altura,
            RadiusX = altura / 2,
            RadiusY = altura / 2,
            Width = 0,
        };
        var calha = new Border { Style = (Style)FindResource("Barra"), Child = barra, Height = altura };

        Grid.SetColumn(valor, 1);
        Grid.SetColumn(calha, 2);
        grelha.Children.Add(rotulo);
        grelha.Children.Add(valor);
        grelha.Children.Add(calha);

        _linhas[metrica.Id] = (valor, barra);
        return grelha;
    }

    private static ToolTip Dica(Metrica metrica) => new()
    {
        Content = new TextBlock
        {
            Text = metrica.Descricao,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 300,
            FontSize = 11.5,
        },
    };

    // ---------- menus ----------

    private void ConstruirMenus()
    {
        menuMostrar.Items.Clear();
        string? grupoAnterior = null;

        foreach (var metrica in _metricas)
        {
            if (grupoAnterior is not null && metrica.Grupo != grupoAnterior)
                menuMostrar.Items.Add(new Separator());
            grupoAnterior = metrica.Grupo;

            var item = new MenuItem
            {
                Header = $"{metrica.Rotulo}   ({metrica.Grupo.ToLowerInvariant()})",
                IsCheckable = true,
                IsChecked = _visiveis!.Contains(metrica.Id),
                Tag = metrica.Id,
                StaysOpenOnClick = true,   // dá para ligar várias sem reabrir o menu
                ToolTip = Dica(metrica),   // passar o rato explica o que é
            };
            ToolTipService.SetShowDuration(item, 30000);
            ToolTipService.SetInitialShowDelay(item, 350);
            item.Click += AlternarMetrica;
            menuMostrar.Items.Add(item);
        }

        menuMostrar.Items.Add(new Separator());
        var explicar = new MenuItem { Header = "O que é cada linha..." };
        explicar.Click += MostrarExplicacao;
        menuMostrar.Items.Add(explicar);

        menuDensidade.Items.Clear();
        foreach (var (chave, titulo) in new[]
                 {
                     ("auto", "Automática (aperta quando é pequeno)"),
                     ("compacta", "Sempre compacta"),
                     ("normal", "Sempre folgada"),
                 })
        {
            var item = new MenuItem
            {
                Header = titulo,
                IsCheckable = true,
                IsChecked = _densidade == chave,
                Tag = chave,
            };
            item.Click += MudarDensidade;
            menuDensidade.Items.Add(item);
        }

        menuIntervalo.Items.Clear();
        foreach (var segundos in new[] { 0.5, 1.0, 2.0, 5.0 })
        {
            var item = new MenuItem
            {
                Header = Intervalo(segundos),
                IsCheckable = true,
                IsChecked = Math.Abs(segundos - _intervalo) < 0.01,
                Tag = segundos,
            };
            item.Click += MudarIntervalo;
            menuIntervalo.Items.Add(item);
        }
    }

    private static string Intervalo(double segundos) =>
        segundos < 1 ? $"{segundos:0.0}s".Replace(".", ",") : $"{segundos:0}s";

    private void AlternarMetrica(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string id }) return;

        if (!_visiveis!.Remove(id)) _visiveis.Add(id);

        GuardarConfig();
        if (_ultima is { } leitura) Aplicar(leitura);
    }

    private void MudarDensidade(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string escolha }) return;

        _densidade = escolha;
        foreach (var item in menuDensidade.Items.OfType<MenuItem>())
            item.IsChecked = (string?)item.Tag == escolha;

        _disposicao = "";                                // forçar reconstrução
        GuardarConfig();
        if (_ultima is { } leitura) Aplicar(leitura);
    }

    private void MudarIntervalo(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: double segundos }) return;

        _intervalo = segundos;
        _relogio.Interval = TimeSpan.FromSeconds(segundos);

        foreach (var item in menuIntervalo.Items.OfType<MenuItem>())
            item.IsChecked = item.Tag is double s && Math.Abs(s - segundos) < 0.01;

        GuardarConfig();
    }

    /// <summary>Janela com a explicação de todas as linhas, mesmo das que estão desligadas.</summary>
    private void MostrarExplicacao(object sender, RoutedEventArgs e)
    {
        var painel = new StackPanel { Margin = new Thickness(20, 16, 20, 20) };
        string? grupoAnterior = null;

        foreach (var metrica in _metricas)
        {
            if (metrica.Grupo != grupoAnterior)
            {
                painel.Children.Add(new TextBlock
                {
                    Text = metrica.Grupo,
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    FontSize = 11,
                    Foreground = Fraco,
                    Margin = new Thickness(0, 14, 0, 6),
                });
                grupoAnterior = metrica.Grupo;
            }

            painel.Children.Add(new TextBlock
            {
                Text = metrica.Rotulo,
                FontFamily = new FontFamily("Segoe UI Semibold"),
                FontSize = 13,
                Foreground = Normal,
            });
            painel.Children.Add(new TextBlock
            {
                Text = metrica.Descricao,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = Fraco,
                Margin = new Thickness(0, 2, 0, 10),
            });
        }

        new Window
        {
            Title = $"{Marca.Nome} — o que é cada linha",
            Width = 460,
            Height = 620,
            Background = Congelado("#121216"),
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = painel },
        }.ShowDialog();
    }

    /// <summary>
    /// Janela "Sobre". Além da identificação, diz de onde estão a vir os números neste
    /// momento — é a primeira coisa que se quer saber quando uma linha não aparece.
    /// </summary>
    private void MostrarSobre(object sender, RoutedEventArgs e)
    {
        var painel = new StackPanel { Margin = new Thickness(24, 20, 24, 22) };

        void Texto(string conteudo, double tamanho, SolidColorBrush cor, string familia = "Segoe UI", double espacoAcima = 0)
            => painel.Children.Add(new TextBlock
            {
                Text = conteudo,
                FontFamily = new FontFamily(familia),
                FontSize = tamanho,
                Foreground = cor,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, espacoAcima, 0, 0),
            });

        Texto(Marca.Nome.ToUpperInvariant(), 22, Normal, "Segoe UI Semibold");
        Texto($"versão {Marca.Versao}  ·  {Marca.Autor}", 12, Fraco, "Segoe UI", 2);
        Texto("Temperaturas da gráfica, uso do processador, memória e discos. " +
              "Um sensor que não exista mesmo aparece como \"n/d\": nunca um zero a fingir.",
              12.5, Legenda, "Segoe UI", 14);

        Texto("DE ONDE VÊM OS NÚMEROS, AGORA", 10, Fraco, "Segoe UI Semibold", 20);

        var l = _ultima;
        var linhas = new (string Fonte, bool Activa, string Detalhe)[]
        {
            ("Gráfica (NVML / API do fabricante)", l?.GpuCore is not null, l?.GpuNome ?? ""),
            ("Limites térmicos da placa (NVML)", l?.GpuLimite is not null,
                l?.GpuLimite is { } limite ? $"estrangula aos {limite:F0} °C" : "indisponível"),
            ("Ponto quente (memória partilhada do HWiNFO)", l?.GpuHotspot is not null,
                l?.GpuHotspot is not null ? "a publicar" : "HWiNFO 8.53+ tem de estar a correr"),
            ("Processador e memória (Windows)", l?.CpuUso is not null, l?.CpuNome ?? ""),
            ("Temperatura do processador (MSR)", l?.CpuTemp is not null,
                l?.CpuTemp is not null ? "a ler" : "driver bloqueado"),
            ("Discos: espaço (Windows)", l?.Discos.Count > 0, $"{l?.Discos.Count ?? 0} volumes"),
            ("Discos: temperatura (SMART)", l?.Discos.Any(d => d.Temperatura is not null) == true,
                l?.Discos.Any(d => d.Temperatura is not null) == true ? "a ler" : "precisa de administrador"),
        };

        foreach (var (fonte, activa, detalhe) in linhas)
        {
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
            linha.Children.Add(new TextBlock
            {
                Text = activa ? "●" : "○",
                Foreground = activa ? Fresco : Fraco,
                FontSize = 11,
                Margin = new Thickness(0, 0, 8, 0),
            });
            linha.Children.Add(new TextBlock { Text = fonte, Foreground = Normal, FontSize = 12 });
            if (!string.IsNullOrWhiteSpace(detalhe))
                linha.Children.Add(new TextBlock
                {
                    Text = $"  —  {detalhe}",
                    Foreground = Fraco,
                    FontSize = 11.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,   // não encostar à margem
                    MaxWidth = 200,
                });
            painel.Children.Add(linha);
        }

        Texto("LICENÇA", 10, Fraco, "Segoe UI Semibold", 20);
        Texto("Vitals é MIT. Usa a LibreHardwareMonitorLib, que é MPL-2.0, sem a modificar. " +
              "Os avisos completos estão em THIRD-PARTY-NOTICES.md, no repositório.",
              11.5, Legenda, "Segoe UI", 4);

        new Window
        {
            Title = $"Sobre o {Marca.Nome}",
            Width = 470,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            Background = Congelado("#121216"),
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = painel,
        }.ShowDialog();
    }

    private void Repor(object sender, RoutedEventArgs e)
    {
        _visiveis = PorOmissao(_metricas);
        _intervalo = 1;
        _relogio.Interval = TimeSpan.FromSeconds(1);
        _menuPronto = false;

        GuardarConfig();
        if (_ultima is { } leitura) Aplicar(leitura);
    }

    // ---------- bandeja do sistema ----------

    private void ConfigurarBandeja()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Mostrar", null, (_, _) => Revelar());
        menu.Items.Add("Sair", null, (_, _) => { _aSairMesmo = true; Close(); });

        _bandeja = new System.Windows.Forms.NotifyIcon
        {
            Icon = IconeDaBandeja(),
            Text = Marca.Nome,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _bandeja.DoubleClick += (_, _) => Revelar();
    }

    private static string TextoDaBandeja(Leitura l)
    {
        var texto = $"GPU {l.GpuCore:F0}°C · CPU {l.CpuUso:F0}% · RAM {l.RamUsada:F0}/{l.RamTotal:F0} GB";
        return texto.Length <= 63 ? texto : texto[..63];   // limite do Windows
    }

    /// <summary>Ícone desenhado em código: um batimento verde em fundo escuro.</summary>
    private static System.Drawing.Icon IconeDaBandeja()
    {
        using var mapa = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(mapa))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            using var fundo = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(230, 18, 18, 22));
            g.FillEllipse(fundo, 0, 0, 31, 31);

            using var caneta = new System.Drawing.Pen(System.Drawing.Color.FromArgb(134, 239, 172), 2.6f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
            };
            g.DrawLines(caneta, new[]
            {
                new System.Drawing.PointF(5, 17), new System.Drawing.PointF(11, 17),
                new System.Drawing.PointF(14, 9), new System.Drawing.PointF(18, 24),
                new System.Drawing.PointF(21, 17), new System.Drawing.PointF(27, 17),
            });
        }

        var apontador = mapa.GetHicon();
        try
        {
            using var temporario = System.Drawing.Icon.FromHandle(apontador);
            return (System.Drawing.Icon)temporario.Clone();
        }
        finally
        {
            DestroyIcon(apontador);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr apontador);

    private void Revelar()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        TrazerParaDentro();
    }

    private void Esconder(object sender, RoutedEventArgs e) => Hide();

    // ---------- janela ----------

    /// <summary>
    /// Arrastar a partir de qualquer ponto. É tratado na pré-visualização porque o
    /// ScrollViewer apanhava o clique antes de ele chegar à janela, e então só se conseguia
    /// arrastar pelas margens. A pega de redimensionar é a única excepção.
    /// </summary>
    private void Arrastar(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origem && DentroDaPega(origem)) return;

        try { DragMove(); } catch (InvalidOperationException) { }
        GuardarConfig();
    }

    private bool DentroDaPega(DependencyObject origem)
    {
        for (var actual = origem; actual is not null; actual = VisualTreeHelper.GetParent(actual))
            if (ReferenceEquals(actual, pega))
                return true;
        return false;
    }

    /// <summary>
    /// Área útil do monitor onde a janela está — e não a do monitor principal.
    /// Com vários ecrãs, usar o principal atirava o widget de volta para o meio.
    /// </summary>
    private Rect AreaUtil()
    {
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var x = (int)((double.IsNaN(Left) ? 0 : Left + Width / 2) * dpi.DpiScaleX);
            var y = (int)((double.IsNaN(Top) ? 0 : Top + 20) * dpi.DpiScaleY);

            var ecra = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(x, y)).WorkingArea;
            return new Rect(
                ecra.Left / dpi.DpiScaleX, ecra.Top / dpi.DpiScaleY,
                ecra.Width / dpi.DpiScaleX, ecra.Height / dpi.DpiScaleY);
        }
        catch (Exception)
        {
            return SystemParameters.WorkArea;
        }
    }

    private void Posicionar(Config config)
    {
        var altura = ActualHeight > 0 ? ActualHeight : 220;

        if (config.Esquerda is { } esq && config.Topo is { } topo)
        {
            // aceita qualquer monitor: só recusa posições sem nenhum ecrã por baixo
            Left = esq;
            Top = topo;

            var area = AreaUtil();
            if (esq + 60 >= area.Left && esq <= area.Right - 60 &&
                topo + 40 >= area.Top && topo <= area.Bottom - 40)
                return;
        }

        var inicial = AreaUtil();
        Left = inicial.Right - Width - 16;   // primeira vez: canto inferior direito
        Top = inicial.Bottom - altura - 16;
    }

    /// <summary>Mantém a janela dentro do monitor em que está quando o tamanho muda.</summary>
    private void TrazerParaDentro()
    {
        if (ActualHeight <= 0 || double.IsNaN(Left) || double.IsNaN(Top)) return;

        var area = AreaUtil();
        var esquerda = Math.Clamp(Left, area.Left + 4, Math.Max(area.Left + 4, area.Right - Width - 4));
        var topo = Math.Clamp(Top, area.Top + 4, Math.Max(area.Top + 4, area.Bottom - ActualHeight - 4));

        if (Math.Abs(esquerda - Left) > 0.5 || Math.Abs(topo - Top) > 0.5)
        {
            Left = esquerda;
            Top = topo;
            GuardarConfig();
        }
    }

    // ---------- redimensionamento manual ----------

    private void ComecarRedimensionar(object sender, MouseButtonEventArgs e)
    {
        _origemRato = e.GetPosition(this);
        _tamanhoInicial = new Size(ActualWidth, ActualHeight);
        _aRedimensionar = true;
        pega.CaptureMouse();
        e.Handled = true;   // não deixar que isto arraste a janela
    }

    private void AoRedimensionar(object sender, MouseEventArgs e)
    {
        if (!_aRedimensionar) return;

        var actual = e.GetPosition(this);
        var largura = Math.Max(MinWidth, _tamanhoInicial.Width + (actual.X - _origemRato.X));
        var altura = Math.Max(MinHeight, _tamanhoInicial.Height + (actual.Y - _origemRato.Y));

        SizeToContent = SizeToContent.Manual;   // a partir daqui manda o utilizador
        Width = largura;
        Height = altura;
    }

    private void AcabarRedimensionar(object sender, MouseButtonEventArgs e)
    {
        if (!_aRedimensionar) return;

        _aRedimensionar = false;
        pega.ReleaseMouseCapture();
        GuardarConfig();

        if (_ultima is { } leitura) Aplicar(leitura);   // pode ter de mudar de colunas
    }

    /// <summary>Volta a deixar a janela encolher ou crescer conforme o que tem para mostrar.</summary>
    private void AjustarAoConteudo(object sender, RoutedEventArgs e)
    {
        SizeToContent = SizeToContent.Height;
        Height = double.NaN;
        _disposicao = "";                               // forçar reconstrução
        if (_ultima is { } leitura) Aplicar(leitura);
        GuardarConfig();
    }

    private void AlternarTopo(object sender, RoutedEventArgs e)
    {
        Topmost = menuTopo.IsChecked;
        GuardarConfig();
    }

    private void AlternarArranque(object sender, RoutedEventArgs e) => DefinirArranque(menuArranque.IsChecked);

    private static void DefinirArranque(bool activo)
    {
        var caminho = Environment.ProcessPath;
        if (caminho is null) return;

        using var chave = Registry.CurrentUser.CreateSubKey(ChaveArranque, writable: true);
        if (chave is null) return;

        if (activo)
        {
            chave.SetValue(Marca.Nome, $"\"{caminho}\"");
            return;
        }

        chave.DeleteValue(Marca.Nome, throwOnMissingValue: false);
        TarefaAgendada($"/delete /tn {Marca.Nome} /f");   // se existir a tarefa elevada, sai também
    }

    /// <summary>
    /// Arranca com o Windows por uma de duas vias: a chave Run, que é simples mas corre sem
    /// elevação (e aí as temperaturas dos discos ficam a "n/d"), ou uma tarefa agendada com
    /// privilégios, que é a forma de as ter. Reconhece as duas para não mostrar desmarcado
    /// o que afinal está a arrancar.
    /// </summary>
    private static bool ArranqueActivo()
    {
        using var chave = Registry.CurrentUser.OpenSubKey(ChaveArranque);
        if (chave?.GetValue(Marca.Nome) is not null) return true;

        return TarefaAgendada("/query /tn " + Marca.Nome);
    }

    private static bool TarefaAgendada(string argumentos)
    {
        try
        {
            using var processo = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("schtasks", argumentos)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
            });

            if (processo is null) return false;
            return processo.WaitForExit(3000) && processo.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void CopiarLeitura(object sender, RoutedEventArgs e)
    {
        if (_ultima is not { } l) return;

        var sb = new StringBuilder();
        sb.AppendLine($"{Marca.Assinatura} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        foreach (var metrica in _metricas)
        {
            var medida = metrica.Ler(l);
            sb.AppendLine($"  {metrica.Grupo,-8} {metrica.Rotulo,-14} {medida?.Texto ?? "n/d"}");
        }

        try { Clipboard.SetText(sb.ToString()); } catch (Exception) { }
    }

    private void Sair(object sender, RoutedEventArgs e)
    {
        _aSairMesmo = true;
        Close();
    }

    /// <summary>Fechar esconde junto às horas; só o "Sair" acaba mesmo com o programa.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_aSairMesmo)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _relogio.Stop();
        GuardarConfig();
        _leitor.Dispose();

        if (_bandeja is not null)
        {
            _bandeja.Visible = false;
            _bandeja.Dispose();
        }

        base.OnClosed(e);
        System.Windows.Application.Current?.Shutdown();
    }

    // ---------- configuração ----------

    private static Config CarregarConfig()
    {
        try
        {
            if (File.Exists(FicheiroConfig))
                return JsonSerializer.Deserialize<Config>(File.ReadAllText(FicheiroConfig)) ?? new Config();
        }
        catch (Exception) { }
        return new Config();
    }

    private void GuardarConfig()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FicheiroConfig)!);
            var config = new Config
            {
                Esquerda = Left,
                Topo = Top,
                SempreAFrente = Topmost,
                Intervalo = _intervalo,
                Visiveis = _visiveis?.ToArray(),
                JaConfigurado = true,
                Largura = SizeToContent == SizeToContent.Manual ? ActualWidth : null,
                Altura = SizeToContent == SizeToContent.Manual ? ActualHeight : null,
                Densidade = _densidade,
            };
            File.WriteAllText(FicheiroConfig, JsonSerializer.Serialize(config,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception) { }
    }

    private static SolidColorBrush Congelado(string hex)
    {
        var cor = (Color)ColorConverter.ConvertFromString(hex)!;
        var pincel = new SolidColorBrush(cor);
        pincel.Freeze();
        return pincel;
    }
}
