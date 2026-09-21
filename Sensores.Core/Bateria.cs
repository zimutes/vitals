using System.Runtime.InteropServices;

namespace Sensores.Core;

/// <summary>
/// Estado da bateria, pelo Windows. Não usa a biblioteca de sensores nem precisa de
/// privilégios: é uma chamada directa ao sistema, e em computadores de secretária
/// responde simplesmente que não há bateria.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class Bateria
{
    [StructLayout(LayoutKind.Sequential)]
    private struct EstadoDeEnergia
    {
        public byte LinhaCorrente;      // 0 = bateria, 1 = ficha, 255 = desconhecido
        public byte EstadoDaBateria;    // 128 = sem bateria
        public byte Percentagem;        // 255 = desconhecido
        public byte Reservado;
        public uint SegundosRestantes;
        public uint SegundosTotais;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out EstadoDeEnergia estado);

    /// <summary>Percentagem de carga e se está a carregar. Ambos null se não houver bateria.</summary>
    public static (float? Carga, bool? ACarregar) Ler()
    {
        try
        {
            if (!GetSystemPowerStatus(out var estado)) return (null, null);
            if ((estado.EstadoDaBateria & 128) != 0) return (null, null);   // sem bateria
            if (estado.Percentagem > 100) return (null, null);              // desconhecido

            return (estado.Percentagem, estado.LinhaCorrente == 1);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }
}
