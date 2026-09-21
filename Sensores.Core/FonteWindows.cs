using LibreHardwareMonitor.Hardware;

namespace Sensores.Core;

/// <summary>Le os sensores de GPU, CPU e RAM. Nao escreve nada no hardware: a
/// biblioteca e usada apenas em modo de leitura, e placa e motherboard ficam desligadas.</summary>
/// <summary>Leitura em Windows, pela LibreHardwareMonitorLib, NVML, WMI e HWiNFO.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class FonteWindows : IFonteDeSensores
{
    private sealed class Visitante : IVisitor
    {
        /// <summary>Ler SMART e ocupacao de discos e caro (chega a custar 8% de um nucleo
        /// se for a cada segundo) e os valores mudam devagar, por isso salta-se quase sempre.</summary>
        public bool IncluirDiscos { get; set; } = true;

        public void VisitComputer(IComputer c) => c.Traverse(this);

        public void VisitHardware(IHardware h)
        {
            if (!IncluirDiscos && h.HardwareType == HardwareType.Storage) return;

            h.Update();
            foreach (var sub in h.SubHardware) sub.Accept(this);
        }

        public void VisitSensor(ISensor s) { }
        public void VisitParameter(IParameter p) { }
    }

    private readonly Computer _computer;
    private readonly Visitante _visitante = new();
    private bool _fechado;

    /// <summary>Limites termicos da placa. Sao constantes, por isso le-se uma vez so.</summary>
    public LimitesNvidia Limites { get; } = new();

    private readonly System.Diagnostics.Stopwatch _cronometro = System.Diagnostics.Stopwatch.StartNew();
    private IReadOnlyList<Disco> _discos = Array.Empty<Disco>();

    // o registo do HWiNFO le-se de dois em dois segundos: nao muda mais depressa que isso
    private readonly System.Diagnostics.Stopwatch _cronometroHwInfo = System.Diagnostics.Stopwatch.StartNew();
    private IReadOnlyList<HwInfo.Publicado> _doHwInfo = Array.Empty<HwInfo.Publicado>();
    private IReadOnlyList<HwInfoMemoria.Leitura> _daMemoriaHwInfo = Array.Empty<HwInfoMemoria.Leitura>();
    private bool _primeiraDoHwInfo = true;

    public FonteWindows()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = false,   // nao tocar no Super I/O
            IsStorageEnabled = true,        // temperatura, ocupacao e actividade dos discos
            IsNetworkEnabled = false,
            IsControllerEnabled = false,
            IsPsuEnabled = false,
            IsBatteryEnabled = false,
        };
        _computer.Open();
        _computer.Accept(_visitante);        // primeira leitura: as cargas precisam de duas amostras
    }

    /// <summary>Periodo de releitura dos discos. Entre releituras usam-se os valores em cache.</summary>
    public TimeSpan PeriodoDosDiscos { get; set; } = TimeSpan.FromSeconds(10);

    public Leitura Ler()
    {
        var relerDiscos = _discos.Count == 0 || _cronometro.Elapsed >= PeriodoDosDiscos;
        _visitante.IncluirDiscos = relerDiscos;

        _computer.Accept(_visitante);

        IHardware? cpu = null, ram = null;
        var placas = new List<IHardware>();
        var sensoresDeDisco = new Dictionary<string, IHardware>(StringComparer.OrdinalIgnoreCase);

        foreach (var h in _computer.Hardware)
        {
            switch (h.HardwareType)
            {
                case HardwareType.Storage:
                    // so ha sensores de disco com privilegios de administrador
                    sensoresDeDisco[h.Name.Trim()] = h;
                    break;
                case HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel:
                    placas.Add(h);
                    break;
                case HardwareType.Cpu:
                    cpu ??= h;
                    break;
                case HardwareType.Memory:
                    // ha dois: "Total Memory" (fisica) e "Virtual Memory" (com ficheiro de paginacao)
                    if (h.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)) ram = h;
                    else ram ??= h;
                    break;
            }
        }

        var gpu = EscolherPlaca(placas);

        if (_primeiraDoHwInfo || _cronometroHwInfo.Elapsed >= TimeSpan.FromSeconds(2))
        {
            _daMemoriaHwInfo = HwInfoMemoria.Ler();
            _doHwInfo = _daMemoriaHwInfo.Count == 0 ? HwInfo.Ler() : Array.Empty<HwInfo.Publicado>();
            _cronometroHwInfo.Restart();
            _primeiraDoHwInfo = false;
        }

        // etiquetas em portugues e ingles, conforme o idioma do HWiNFO.
        // a memoria partilhada e mais rica; o registo (gadget) fica como alternativa
        var hotspot = HwInfoMemoria.Procurar(_daMemoriaHwInfo, ["GPU", "NVIDIA", "Radeon"],
                          "ponto quente", "hot spot", "hotspot")
                   ?? HwInfo.Procurar(_doHwInfo, "ponto quente", "hot spot", "hotspot");

        var ramUsada = Valor(ram, SensorType.Data, "Memory Used");
        var ramLivre = Valor(ram, SensorType.Data, "Memory Available");
        var (bateria, aCarregar) = Core.Bateria.Ler();

        return new Leitura
        {
            GpuNome = Nome(gpu, "GPU"),
            // os nomes mudam entre NVIDIA, AMD e Intel: tenta-se por ordem
            GpuCore = Valor(gpu, SensorType.Temperature, "GPU Core", "GPU Temperature", "GPU Package"),
            GpuJuncao = Valor(gpu, SensorType.Temperature, "GPU Memory Junction", "GPU Hot Spot", "GPU Junction"),
            GpuUso = Valor(gpu, SensorType.Load, "GPU Core", "D3D 3D", "GPU Total"),
            GpuVramUsada = Valor(gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used"),
            GpuVramTotal = Valor(gpu, SensorType.SmallData, "GPU Memory Total"),
            GpuPotencia = Valor(gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU PPT"),
            GpuVentoinha = Valor(gpu, SensorType.Fan, "GPU Fan", "GPU Fan 1")
                        ?? Primeiro(gpu, SensorType.Fan),
            GpuLimite = Limites.MaxOperacional,
            GpuHotspot = hotspot,

            CpuNome = Nome(cpu, "CPU"),
            CpuUso = Valor(cpu, SensorType.Load, "CPU Total"),
            CpuTemp = Valor(cpu, SensorType.Temperature, "Core (Tctl/Tdie)")
                   ?? Valor(cpu, SensorType.Temperature, "CPU Package")
                   ?? Valor(cpu, SensorType.Temperature, "Core Average"),

            RamUsada = ramUsada,
            RamTotal = ramUsada is { } u && ramLivre is { } l ? u + l : null,

            Discos = GuardarDiscos(sensoresDeDisco, relerDiscos),

            Bateria = bateria,
            BateriaACarregar = aCarregar,
        };
    }

    /// <summary>
    /// Num portatil ha duas placas: a integrada e a dedicada. Mostrar a integrada nao serve
    /// de nada a quem quer ver a temperatura enquanto joga ou renderiza, por isso prefere-se
    /// a dedicada; entre duas do mesmo tipo, a que tiver mais memoria.
    /// </summary>
    private static IHardware? EscolherPlaca(List<IHardware> placas) => placas
        .OrderByDescending(h => h.HardwareType switch
        {
            HardwareType.GpuNvidia => 3,
            HardwareType.GpuAmd => 2,
            HardwareType.GpuIntel => 1,
            _ => 0,
        })
        .ThenByDescending(h => Valor(h, SensorType.SmallData, "GPU Memory Total") ?? 0)
        .FirstOrDefault();

    /// <summary>
    /// Uma linha por volume. O espaço vem do Windows e está sempre disponível; a temperatura
    /// e a actividade vêm do SMART e só aparecem com privilégios de administrador.
    /// </summary>
    private IReadOnlyList<Disco> GuardarDiscos(Dictionary<string, IHardware> sensores, bool relerDiscos)
    {
        if (!relerDiscos) return _discos;

        var lidos = new List<Disco>();
        var jaComSensor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var volume in MapaDeDiscos.Volumes())
        {
            sensores.TryGetValue(volume.Modelo, out var hardware);

            float? usado = null, total = null;
            try
            {
                var unidade = new DriveInfo(volume.Letra);
                if (unidade.IsReady)
                {
                    total = (float)(unidade.TotalSize / 1e9);
                    usado = total - (float)(unidade.TotalFreeSpace / 1e9);
                }
            }
            catch (Exception) { }

            // leitores de cartoes vazios e volumes minusculos so fazem barulho
            if (total is null or < 1f) continue;

            var primeiroDoDisco = jaComSensor.Add(volume.Modelo);

            lidos.Add(new Disco
            {
                Id = volume.Letra,
                Modelo = volume.Modelo,
                Etiqueta = volume.Letra,
                // o NVMe chama-lhe "Composite Temperature"; os discos rigidos so "Temperature"
                Temperatura = Valor(hardware, SensorType.Temperature, "Composite Temperature")
                           ?? Valor(hardware, SensorType.Temperature, "Temperature"),
                Actividade = Valor(hardware, SensorType.Load, "Total Activity"),
                TemSensorProprio = primeiroDoDisco,
                // os NVMe publicam os proprios limites (tipicamente 89 e 93)
                LimiteAviso = Valor(hardware, SensorType.Temperature, "Warning Temperature"),
                LimiteCritico = Valor(hardware, SensorType.Temperature, "Critical Temperature"),
                UsadoGB = usado,
                TotalGB = total,
            });
        }

        _discos = lidos.OrderBy(d => d.Etiqueta, StringComparer.OrdinalIgnoreCase).ToList();
        _cronometro.Restart();
        return _discos;
    }

    /// <summary>Todos os sensores encontrados, para se poder confirmar de onde vem cada numero.</summary>
    public IEnumerable<(string Hardware, string Tipo, string Sensor, float? Valor)> Inventario()
    {
        _computer.Accept(_visitante);
        foreach (var h in _computer.Hardware)
            foreach (var s in h.Sensors.OrderBy(s => s.SensorType.ToString()).ThenBy(s => s.Name))
                yield return (h.Name, s.SensorType.ToString(), s.Name, s.Value);
    }

    private static string Nome(IHardware? h, string fallback) => h?.Name ?? fallback;

    /// <summary>Procura um sensor pelo nome exacto. Trata 0 numa temperatura como ausencia:
    /// nesta maquina o Tctl do Ryzen devolve 0,0 por o driver de MSR estar bloqueado, e mostrar
    /// "0 graus" seria pior que nao mostrar nada.</summary>
    private static float? Valor(IHardware? h, SensorType tipo, params string[] nomes)
    {
        if (h is null) return null;

        foreach (var nome in nomes)
            foreach (var s in h.Sensors)
            {
                if (s.SensorType != tipo || !string.Equals(s.Name, nome, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Aceitavel(s, tipo) is { } v) return v;
            }

        return null;
    }

    /// <summary>Ultimo recurso: o primeiro sensor deste tipo, seja qual for o nome.</summary>
    private static float? Primeiro(IHardware? h, SensorType tipo)
    {
        if (h is null) return null;
        foreach (var s in h.Sensors)
            if (s.SensorType == tipo && Aceitavel(s, tipo) is { } v)
                return v;
        return null;
    }

    private static float? Aceitavel(ISensor s, SensorType tipo)
    {
        var v = s.Value;
        if (v is null or float.NaN) return null;
        if (tipo == SensorType.Temperature && v <= 0f) return null;
        return v;
    }

    public void Dispose()
    {
        if (_fechado) return;
        _fechado = true;
        _computer.Close();
    }
}
