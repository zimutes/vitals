using Microsoft.Win32;

namespace Sensores.Core;

/// <summary>
/// Lê valores publicados pelo HWiNFO no registo do Windows.
///
/// Porquê: há sensores que nenhuma API pública da NVIDIA expõe — o hotspot do chip e a
/// temperatura de cada módulo de memória GDDR7. O HWiNFO lê-os com driver próprio, a partir
/// dos registos da placa. Verificado nesta máquina numa RTX 5070: a NVAPI
/// (<c>ThermChannelGetStatus</c>) só devolve dois canais, core e junção de memória, enquanto
/// o HWiNFO mostra também "ponto quente" e sete sensores de memória.
///
/// Não é preciso ter o HWiNFO: sem ele, tudo isto fica a null e as linhas desaparecem.
/// Com ele a correr e com "Report value to Registry" activo nos sensores desejados, os
/// valores aparecem sozinhos.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class HwInfo
{
    private const string Chave = @"Software\HWiNFO64\VSB";

    public sealed record Publicado(string Sensor, string Etiqueta, float Valor);

    /// <summary>Tudo o que o HWiNFO está a publicar neste momento.</summary>
    public static IReadOnlyList<Publicado> Ler()
    {
        var lista = new List<Publicado>();
        try
        {
            using var chave = Registry.CurrentUser.OpenSubKey(Chave);
            if (chave is null) return lista;

            // o HWiNFO numera as entradas: Sensor0/Label0/Value0/ValueRaw0, Sensor1/...
            for (var i = 0; i < 100; i++)
            {
                var etiqueta = chave.GetValue($"Label{i}") as string;
                if (string.IsNullOrWhiteSpace(etiqueta)) continue;

                var numero = Numero(chave.GetValue($"ValueRaw{i}") as string)
                          ?? Numero(chave.GetValue($"Value{i}") as string);
                if (numero is null) continue;

                lista.Add(new Publicado(chave.GetValue($"Sensor{i}") as string ?? "", etiqueta.Trim(), numero.Value));
            }
        }
        catch (Exception)
        {
            // sem HWiNFO, sem publicação, ou sem permissões: fica vazio
        }
        return lista;
    }

    /// <summary>Primeiro valor cuja etiqueta contenha um dos termos (sem distinguir acentos
    /// de maiúsculas). Aceita vários porque o HWiNFO traduz as etiquetas.</summary>
    public static float? Procurar(IReadOnlyList<Publicado> publicados, params string[] termos)
    {
        foreach (var termo in termos)
            foreach (var p in publicados)
                if (p.Etiqueta.Contains(termo, StringComparison.OrdinalIgnoreCase))
                    return p.Valor;
        return null;
    }

    private static float? Numero(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;

        // "54.6 °C" ou "54,6 °C" -> 54.6
        var limpo = new string(texto.TakeWhile(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray())
            .Replace(',', '.');

        return float.TryParse(limpo, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var valor) ? valor : null;
    }
}
