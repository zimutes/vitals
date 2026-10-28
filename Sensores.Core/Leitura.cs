namespace Sensores.Core;

/// <summary>Uma fotografia dos sensores num instante. Null = sensor inexistente ou sem valor
/// fiavel; quem mostra deve escrever "n/d" e nunca zero.</summary>
public sealed record Leitura
{
    public string GpuNome { get; init; } = "GPU";

    /// <summary>
    /// Temperatura geral do chip, no sensor de referencia: a que a NVIDIA, o nvidia-smi e o
    /// Gestor de Tarefas mostram (na AMD, a "edge"). Chama-se GpuCore por compatibilidade com
    /// o --json, mas nao e o ponto mais quente do chip: esse e o <see cref="GpuHotspot"/>.
    /// </summary>
    public float? GpuCore { get; init; }

    /// <summary>
    /// Temperatura de juncao da memoria GDDR (o ponto mais quente da VRAM). Nunca o hotspot
    /// do chip, mesmo nas AMD, onde o kernel chama "junction" ao hotspot.
    /// </summary>
    public float? GpuJuncao { get; init; }
    public float? GpuUso { get; init; }
    public float? GpuVramUsada { get; init; }   // MB
    public float? GpuVramTotal { get; init; }   // MB
    public float? GpuPotencia { get; init; }    // W
    public float? GpuVentoinha { get; init; }   // rpm

    /// <summary>Temperatura a que a placa comeca a estrangular (85 na RTX 5070).</summary>
    public float? GpuLimite { get; init; }

    /// <summary>Graus que faltam ate ao limite: o "T.Limit" do nvidia-smi. Verificado:
    /// 85 - 48 = 37.</summary>
    public float? GpuMargem => GpuLimite is { } limite && GpuCore is { } core ? limite - core : null;

    /// <summary>
    /// Ponto quente do chip. As AMD e as NVIDIA ate a serie 40 publicam-no; nas RTX 50 a
    /// NVIDIA tirou-o da NVAPI e so se le directamente dos registos da placa, com driver de
    /// kernel, por isso vem do HWiNFO quando esta a correr e a publicar.
    /// </summary>
    public float? GpuHotspot { get; init; }

    /// <summary>
    /// Hotspot menos temperatura geral: o "delta hotspot" dos foruns, que mede a qualidade da
    /// transferencia de calor. Sem hotspot fica a null; a juncao da memoria nao o substitui,
    /// porque e outro componente.
    /// </summary>
    public float? GpuDeltaHotspot => GpuHotspot is { } h && GpuCore is { } c ? h - c : null;

    public string CpuNome { get; init; } = "CPU";
    public float? CpuUso { get; init; }
    public float? CpuTemp { get; init; }        // normalmente null nesta maquina (driver MSR bloqueado)

    public float? RamUsada { get; init; }       // GB
    public float? RamTotal { get; init; }       // GB

    public IReadOnlyList<Disco> Discos { get; init; } = Array.Empty<Disco>();

    /// <summary>Carga da bateria em percentagem. Null em computadores de secretaria.</summary>
    public float? Bateria { get; init; }
    public bool? BateriaACarregar { get; init; }

    public float? RamUso => RamUsada is { } u && RamTotal is { } t && t > 0 ? u / t * 100f : null;
    public float? GpuVramUso => GpuVramUsada is { } u && GpuVramTotal is { } t && t > 0 ? u / t * 100f : null;
}
