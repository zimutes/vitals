using System.Globalization;

namespace Sensores.Core;

/// <summary>
/// Leitura em Linux. Não precisa de bibliotecas, drivers nem privilégios: o kernel publica
/// tudo em ficheiros de texto.
///
///   /sys/class/hwmon/hwmon*/         temperaturas, ventoinhas e potências, por chip
///   /proc/stat                       tempo do processador, para calcular a carga
///   /proc/meminfo                    memória
///   /sys/class/power_supply/BAT*/    bateria
///
/// Ao contrário do Windows, aqui o ponto quente das placas AMD vem de graça: o controlador
/// amdgpu publica <c>temp2_input</c> como "junction". Nas NVIDIA continua a não existir,
/// porque é o controlador da NVIDIA que não o expõe, em qualquer sistema.
/// </summary>
internal sealed class FonteLinux : IFonteDeSensores
{
    private const string Hwmon = "/sys/class/hwmon";

    private sealed record Chip(string Nome, string Caminho);

    private readonly List<Chip> _chips = new();
    private readonly LimitesNvidia _limites = new();

    private long _trabalhoAnterior;
    private long _totalAnterior;

    public FonteLinux()
    {
        if (!Directory.Exists(Hwmon)) return;

        foreach (var pasta in Directory.EnumerateDirectories(Hwmon))
        {
            var nome = Texto(Path.Combine(pasta, "name"));
            if (!string.IsNullOrWhiteSpace(nome)) _chips.Add(new Chip(nome, pasta));
        }

        LerCargaDoProcessador();   // primeira amostra: a carga é um delta
    }

    public Leitura Ler()
    {
        var (ramUsada, ramTotal) = LerMemoria();
        var (bateria, aCarregar) = LerBateria();

        var nvidia = PrimeiroChip("nvidia");
        var amd = PrimeiroChip("amdgpu");
        var gpu = amd ?? nvidia;

        return new Leitura
        {
            GpuNome = NomeDaGrafica(amd, nvidia),

            // nas AMD o kernel numera: 1 = borda (core), 2 = junção, 3 = memória
            GpuCore = amd is not null ? Temperatura(amd, 1) : TemperaturaNvidia(),
            GpuJuncao = amd is not null ? Temperatura(amd, 2) ?? Temperatura(amd, 3) : null,
            GpuUso = amd is not null ? Percentagem("/sys/class/drm/card0/device/gpu_busy_percent") : UsoNvidia(),
            GpuVramUsada = amd is not null ? MegaBytes("/sys/class/drm/card0/device/mem_info_vram_used") : null,
            GpuVramTotal = amd is not null ? MegaBytes("/sys/class/drm/card0/device/mem_info_vram_total") : null,
            GpuPotencia = gpu is not null ? MicroWatts(gpu, 1) : null,
            GpuVentoinha = gpu is not null ? Numero(Path.Combine(gpu.Caminho, "fan1_input")) : null,
            GpuLimite = _limites.MaxOperacional,

            CpuNome = NomeDoProcessador(),
            CpuUso = LerCargaDoProcessador(),
            CpuTemp = TemperaturaDoProcessador(),

            RamUsada = ramUsada,
            RamTotal = ramTotal,

            Discos = LerDiscos(),

            Bateria = bateria,
            BateriaACarregar = aCarregar,
        };
    }

    public IEnumerable<(string Hardware, string Tipo, string Sensor, float? Valor)> Inventario()
    {
        foreach (var chip in _chips)
            foreach (var ficheiro in Directory.EnumerateFiles(chip.Caminho, "*_input").OrderBy(f => f))
            {
                var nome = Path.GetFileNameWithoutExtension(ficheiro).Replace("_input", "");
                var etiqueta = Texto(ficheiro.Replace("_input", "_label"));
                var bruto = Numero(ficheiro);
                if (bruto is null) continue;

                var (tipo, valor) = nome switch
                {
                    var n when n.StartsWith("temp") => ("Temperature", bruto / 1000f),
                    var n when n.StartsWith("fan") => ("Fan", bruto),
                    var n when n.StartsWith("power") => ("Power", bruto / 1_000_000f),
                    var n when n.StartsWith("in") => ("Voltage", bruto / 1000f),
                    _ => ("Outro", bruto),
                };

                yield return (chip.Nome, tipo, string.IsNullOrWhiteSpace(etiqueta) ? nome : $"{nome} ({etiqueta})", valor);
            }
    }

    // ---------- processador ----------

    /// <summary>Carga a partir do delta de /proc/stat entre duas leituras.</summary>
    private float? LerCargaDoProcessador()
    {
        var linha = Linhas("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
        if (linha is null) return null;

        var campos = linha.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(c => long.TryParse(c, out var v) ? v : 0).ToArray();
        if (campos.Length < 4) return null;

        var total = campos.Sum();
        var ocioso = campos[3] + (campos.Length > 4 ? campos[4] : 0);   // idle + iowait
        var trabalho = total - ocioso;

        var deltaTotal = total - _totalAnterior;
        var deltaTrabalho = trabalho - _trabalhoAnterior;
        _totalAnterior = total;
        _trabalhoAnterior = trabalho;

        return deltaTotal > 0 ? (float)(100.0 * deltaTrabalho / deltaTotal) : null;
    }

    private float? TemperaturaDoProcessador()
    {
        // k10temp (AMD), zenpower (AMD com módulo à parte), coretemp (Intel)
        foreach (var nome in new[] { "k10temp", "zenpower", "coretemp" })
        {
            var chip = PrimeiroChip(nome);
            if (chip is null) continue;

            // preferir a entrada com etiqueta Tdie / Tctl / Package
            for (var i = 1; i <= 8; i++)
            {
                var etiqueta = Texto(Path.Combine(chip.Caminho, $"temp{i}_label"));
                if (etiqueta is null) continue;
                if (etiqueta.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
                    etiqueta.Contains("Tctl", StringComparison.OrdinalIgnoreCase) ||
                    etiqueta.Contains("Package", StringComparison.OrdinalIgnoreCase))
                    return Temperatura(chip, i);
            }

            return Temperatura(chip, 1);
        }
        return null;
    }

    private static string NomeDoProcessador()
    {
        var linha = Linhas("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name"));
        var nome = linha?.Split(':', 2).ElementAtOrDefault(1)?.Trim();
        return string.IsNullOrWhiteSpace(nome) ? "CPU" : nome;
    }

    // ---------- gráfica ----------

    private string NomeDaGrafica(Chip? amd, Chip? nvidia)
    {
        if (amd is not null)
        {
            var modelo = Texto("/sys/class/drm/card0/device/product_name");
            return string.IsNullOrWhiteSpace(modelo) ? "GPU AMD" : modelo;
        }
        return nvidia is not null || _limites.Disponivel ? "GPU NVIDIA" : "GPU";
    }

    private float? TemperaturaNvidia()
    {
        var chip = PrimeiroChip("nvidia");
        return chip is not null ? Temperatura(chip, 1) : null;
    }

    private static float? UsoNvidia() => null;   // sem NVML de carga, fica em branco

    // ---------- memória ----------

    private static (float? Usada, float? Total) LerMemoria()
    {
        float? total = null, disponivel = null;
        foreach (var linha in Linhas("/proc/meminfo"))
        {
            if (linha.StartsWith("MemTotal:")) total = KiloBytes(linha);
            else if (linha.StartsWith("MemAvailable:")) disponivel = KiloBytes(linha);
            if (total is not null && disponivel is not null) break;
        }
        return total is { } t && disponivel is { } d ? (t - d, t) : (null, null);

        static float? KiloBytes(string linha)
        {
            var campos = linha.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return campos.Length >= 2 && long.TryParse(campos[1], out var kb) ? kb / 1_048_576f : null;
        }
    }

    // ---------- discos ----------

    private IReadOnlyList<Disco> LerDiscos()
    {
        var discos = new List<Disco>();
        var temperaturas = TemperaturasDosDiscos();
        var jaVisto = new HashSet<string>();

        foreach (var unidade in DriveInfo.GetDrives())
        {
            try
            {
                if (!unidade.IsReady || unidade.DriveType != DriveType.Fixed) continue;

                // fora pseudo-sistemas: overlay de contentores, snaps, tmpfs
                if (unidade.DriveFormat is "overlay" or "squashfs" or "tmpfs" or "devtmpfs") continue;

                // a partição de arranque não interessa a ninguém: é minúscula e não muda
                if (unidade.Name.StartsWith("/boot", StringComparison.Ordinal)) continue;

                var total = (float)(unidade.TotalSize / 1e9);
                if (total < 2f) continue;
                if (!jaVisto.Add(unidade.Name)) continue;

                temperaturas.TryGetValue(unidade.Name, out var temperatura);

                discos.Add(new Disco
                {
                    Id = unidade.Name,
                    Modelo = unidade.DriveFormat,
                    Etiqueta = unidade.Name,
                    Temperatura = temperatura,
                    UsadoGB = total - (float)(unidade.TotalFreeSpace / 1e9),
                    TotalGB = total,
                    TemSensorProprio = temperatura is not null,
                });
            }
            catch (Exception) { }
        }

        // as temperaturas que não se conseguiram atribuir a um ponto de montagem vão à parte
        foreach (var (nome, valor) in temperaturas.Where(t => t.Key.StartsWith("/dev/")))
            discos.Add(new Disco { Id = nome, Modelo = nome, Etiqueta = nome, Temperatura = valor });

        return discos.OrderBy(d => d.Etiqueta, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Temperaturas dos discos: os NVMe publicam-nas no hwmon com o nome "nvme", e os SATA
    /// com o módulo drivetemp carregado. A ligação ao ponto de montagem é aproximada — o
    /// primeiro disco fica associado à raiz, que é o caso comum num servidor.
    /// </summary>
    private Dictionary<string, float?> TemperaturasDosDiscos()
    {
        var mapa = new Dictionary<string, float?>();
        var primeiro = true;

        foreach (var chip in _chips.Where(c => c.Nome is "nvme" or "drivetemp"))
        {
            var temperatura = Temperatura(chip, 1);
            if (temperatura is null) continue;

            if (primeiro) { mapa["/"] = temperatura; primeiro = false; }
            else mapa[$"/dev/{chip.Nome}{mapa.Count}"] = temperatura;
        }

        return mapa;
    }

    // ---------- bateria ----------

    private static (float? Carga, bool? ACarregar) LerBateria()
    {
        try
        {
            const string Fontes = "/sys/class/power_supply";
            if (!Directory.Exists(Fontes)) return (null, null);

            var bateria = Directory.EnumerateDirectories(Fontes)
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith("BAT", StringComparison.OrdinalIgnoreCase));
            if (bateria is null) return (null, null);

            var carga = Numero(Path.Combine(bateria, "capacity"));
            var estado = Texto(Path.Combine(bateria, "status"));

            return (carga, estado?.Equals("Charging", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    // ---------- utilitários ----------

    private Chip? PrimeiroChip(string nome) =>
        _chips.FirstOrDefault(c => c.Nome.Equals(nome, StringComparison.OrdinalIgnoreCase));

    private static float? Temperatura(Chip chip, int indice)
    {
        var valor = Numero(Path.Combine(chip.Caminho, $"temp{indice}_input"));
        return valor is { } v && v > 0 ? v / 1000f : null;      // o kernel dá milésimos de grau
    }

    private static float? MicroWatts(Chip chip, int indice)
    {
        var valor = Numero(Path.Combine(chip.Caminho, $"power{indice}_average"))
                 ?? Numero(Path.Combine(chip.Caminho, $"power{indice}_input"));
        return valor is { } v && v > 0 ? v / 1_000_000f : null;
    }

    private static float? Percentagem(string caminho) => Numero(caminho);

    private static float? MegaBytes(string caminho)
    {
        var valor = Numero(caminho);
        return valor is { } v ? v / 1_048_576f : null;
    }

    private static float? Numero(string caminho)
    {
        var texto = Texto(caminho);
        return float.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor) ? valor : null;
    }

    private static string? Texto(string caminho)
    {
        try
        {
            return File.Exists(caminho) ? File.ReadAllText(caminho).Trim() : null;
        }
        catch (Exception)
        {
            return null;   // alguns ficheiros do sysfs devolvem erro conforme o estado do dispositivo
        }
    }

    private static IEnumerable<string> Linhas(string caminho)
    {
        string[] linhas;
        try
        {
            linhas = File.Exists(caminho) ? File.ReadAllLines(caminho) : Array.Empty<string>();
        }
        catch (Exception)
        {
            linhas = Array.Empty<string>();
        }
        return linhas;
    }

    public void Dispose() { }
}
