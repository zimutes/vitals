namespace Sensores.Core;

/// <summary>Uma fotografia dos sensores num instante. Null = sensor inexistente ou sem valor
/// fiavel; quem mostra deve escrever "n/d" e nunca zero.</summary>
public sealed record Leitura
{
    public string GpuNome { get; init; } = "GPU";
    public float? GpuCore { get; init; }
    public float? GpuJuncao { get; init; }
    public float? GpuUso { get; init; }
    public float? GpuVramUsada { get; init; }   // MB
    public float? GpuVramTotal { get; init; }   // MB
    public float? GpuPotencia { get; init; }    // W
    public float? GpuVentoinha { get; init; }   // rpm

    /// <summary>Temperatura a que a placa comeca a estrangular (85 na RTX 5070).</summary>
    public float? GpuLimite { get; init; }

    /// <summary>Graus que faltam ate ao limite: o "T.Limit" do nvidia-smi, e o mais proximo
    /// de um hotspot que as GeForce recentes expoem. Verificado: 85 - 48 = 37.</summary>
    public float? GpuMargem => GpuLimite is { } limite && GpuCore is { } core ? limite - core : null;

    /// <summary>
    /// Ponto quente do chip. A NVIDIA nao o expoe em API nenhuma nas GeForce recentes;
    /// so aparece se o HWiNFO estiver a correr e a publicar no registo.
    /// </summary>
    public float? GpuHotspot { get; init; }

    /// <summary>
    /// Diferenca entre o ponto de referencia mais quente e o core. E o "delta hotspot" dos
    /// foruns: usa o hotspot quando existe, e a juncao de memoria quando nao existe.</summary>
    public float? GpuDeltaHotspot => GpuHotspot is { } h && GpuCore is { } c ? h - c : null;

    /// <summary>
    /// Diferenca entre a juncao e o core. E o "delta hotspot" de que se fala nos foruns:
    /// mede a qualidade da transferencia de calor. Nesta placa a juncao e a da memoria
    /// (a NVIDIA nao expoe hotspot do core na serie 50); em placas que exponham hotspot,
    /// o valor passa a ser hotspot menos core sem ser preciso mudar nada.
    /// </summary>
    public float? GpuDelta => GpuJuncao is { } juncao && GpuCore is { } core ? juncao - core : null;

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
