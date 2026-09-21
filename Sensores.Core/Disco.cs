using System.Management;

namespace Sensores.Core;

public sealed record Disco
{
    /// <summary>Identificador estável (a letra do volume). É a chave com que as preferências
    /// guardam as linhas escolhidas.</summary>
    public string Id { get; init; } = "";

    /// <summary>Modelo do disco físico onde o volume está.</summary>
    public string Modelo { get; init; } = "";

    /// <summary>"C:" — o que se mostra.</summary>
    public string Etiqueta { get; init; } = "";

    /// <summary>Só com privilégios de administrador: ler SMART exige-o.</summary>
    public float? Temperatura { get; init; }

    /// <summary>Idem.</summary>
    public float? Actividade { get; init; }

    /// <summary>
    /// Falso nas outras partições do mesmo disco físico. A temperatura é do disco, não do
    /// volume: mostrá-la em H:, I: e J: seria repetir três vezes o mesmo número.
    /// </summary>
    public bool TemSensorProprio { get; init; } = true;

    /// <summary>Limites que o próprio disco publica (os NVMe trazem-nos no SMART: tipicamente
    /// aviso aos 89 °C e crítico aos 93). Quando não existem, quem mostra usa limiares
    /// conservadores.</summary>
    public float? LimiteAviso { get; init; }
    public float? LimiteCritico { get; init; }

    /// <summary>Espaço: vem do Windows, sem precisar de privilégios.</summary>
    public float? UsadoGB { get; init; }
    public float? TotalGB { get; init; }

    public float? UsoEspaco => UsadoGB is { } u && TotalGB is { } t && t > 0 ? u / t * 100f : null;
    public float? LivreGB => UsadoGB is { } u && TotalGB is { } t ? t - u : null;
}

/// <summary>
/// Cruza os volumes do Windows com os discos físicos, pelo WMI.
///
/// Serve dois propósitos: dar às linhas a letra que o utilizador conhece ("C:") em vez do
/// modelo, e deixar de fora unidades virtuais — o Google Drive aparece como disco fixo de
/// 999 GB no <c>DriveInfo</c>, mas não tem partição física por trás, por isso cai fora.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class MapaDeDiscos
{
    public sealed record Volume(string Letra, string Modelo);

    private static IReadOnlyList<Volume>? _cache;

    public static IReadOnlyList<Volume> Volumes() => _cache ??= Descobrir();

    private static List<Volume> Descobrir()
    {
        var volumes = new List<Volume>();
        try
        {
            // tem de ser SELECT *: um objecto parcial vem sem a chave e o GetRelated falha
            using var pesquisa = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (var objecto in pesquisa.Get())
            {
                if (objecto is not ManagementObject disco) continue;

                var modelo = disco["Model"]?.ToString()?.Trim() ?? "";

                foreach (var p in disco.GetRelated("Win32_DiskPartition"))
                {
                    if (p is not ManagementObject particao) continue;
                    foreach (var v in particao.GetRelated("Win32_LogicalDisk"))
                        if (v["DeviceID"]?.ToString() is { Length: > 0 } letra)
                            volumes.Add(new Volume(letra, modelo));
                }
            }
        }
        catch (Exception)
        {
            // sem WMI não há letras; o Leitor cai para os discos que a biblioteca vir
        }
        return volumes;
    }
}
