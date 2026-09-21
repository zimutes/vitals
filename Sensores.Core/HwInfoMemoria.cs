using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace Sensores.Core;

/// <summary>
/// Lê a memória partilhada do HWiNFO.
///
/// É a via mais rica para os sensores que a NVIDIA não expõe: hotspot do chip e a
/// temperatura de cada módulo GDDR7. O HWiNFO lê-os com driver próprio e publica-os aqui,
/// com nome, unidade e valores mínimo/máximo/médio.
///
/// Na versão gratuita esta publicação dura 12 horas por arranque do HWiNFO; passado esse
/// tempo o mapa deixa de ser actualizado e as linhas desaparecem sozinhas, como qualquer
/// outro sensor em falta. Activa-se no HWiNFO em Configurações principais →
/// "Suporte de memória compartilhada".
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class HwInfoMemoria
{
    private const string Mapa = "Global\\HWiNFO_SENS_SM2";
    private const uint Assinatura = 0x53695748;   // "SiWH" em little endian

    // Pack = 1 é essencial: sem isto o .NET alinha o campo de 8 bytes a 8 e mete 4 bytes de
    // enchimento antes dele, o que desloca todos os deslocamentos seguintes e faz ler lixo
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Cabecalho
    {
        public uint Assinatura;
        public uint Versao;
        public uint Revisao;
        public long Instante;
        public uint DeslocamentoSensores;
        public uint TamanhoSensor;
        public uint NumeroSensores;
        public uint DeslocamentoLeituras;
        public uint TamanhoLeitura;
        public uint NumeroLeituras;
    }

    public sealed record Leitura(string Sensor, string Etiqueta, string Unidade, double Valor);

    /// <summary>Todas as leituras publicadas, ou lista vazia se o HWiNFO não estiver a publicar.</summary>
    public static IReadOnlyList<Leitura> Ler()
    {
        try
        {
            using var mapa = MemoryMappedFile.OpenExisting(Mapa, MemoryMappedFileRights.Read);
            using var vista = mapa.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            vista.Read<Cabecalho>(0, out var cabecalho);
            if (cabecalho.Assinatura != Assinatura) return Array.Empty<Leitura>();
            if (cabecalho.NumeroLeituras is 0 or > 10000) return Array.Empty<Leitura>();

            // nomes dos blocos de sensores (a GPU, a motherboard, cada disco...)
            var nomes = new string[cabecalho.NumeroSensores];
            for (uint i = 0; i < cabecalho.NumeroSensores; i++)
            {
                var baseSensor = cabecalho.DeslocamentoSensores + i * cabecalho.TamanhoSensor;
                // 4 + 4 identificadores, depois o nome original e o nome dado pelo utilizador
                var original = Texto(vista, baseSensor + 8, 128);
                var doUtilizador = Texto(vista, baseSensor + 8 + 128, 128);
                nomes[i] = string.IsNullOrWhiteSpace(doUtilizador) ? original : doUtilizador;
            }

            var leituras = new List<Leitura>((int)cabecalho.NumeroLeituras);
            for (uint i = 0; i < cabecalho.NumeroLeituras; i++)
            {
                var baseLeitura = cabecalho.DeslocamentoLeituras + i * cabecalho.TamanhoLeitura;

                var indiceSensor = vista.ReadUInt32(baseLeitura + 4);
                var etiquetaOriginal = Texto(vista, baseLeitura + 12, 128);
                var etiquetaUtilizador = Texto(vista, baseLeitura + 12 + 128, 128);
                var unidade = Texto(vista, baseLeitura + 12 + 256, 16);
                var valor = vista.ReadDouble(baseLeitura + 12 + 256 + 16);

                leituras.Add(new Leitura(
                    indiceSensor < nomes.Length ? nomes[indiceSensor] : "",
                    string.IsNullOrWhiteSpace(etiquetaUtilizador) ? etiquetaOriginal : etiquetaUtilizador,
                    unidade,
                    valor));
            }

            return leituras;
        }
        catch (Exception)
        {
            // sem HWiNFO, sem publicação, ou sem permissões para o mapa
            return Array.Empty<Leitura>();
        }
    }

    /// <summary>Primeira leitura de um sensor cujo nome e etiqueta contenham os termos dados.</summary>
    public static float? Procurar(IReadOnlyList<Leitura> leituras, string[] doSensor, params string[] daEtiqueta)
    {
        foreach (var termo in daEtiqueta)
            foreach (var leitura in leituras)
            {
                if (!leitura.Etiqueta.Contains(termo, StringComparison.OrdinalIgnoreCase)) continue;
                if (doSensor.Length > 0 &&
                    !doSensor.Any(s => leitura.Sensor.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;

                return (float)leitura.Valor;
            }
        return null;
    }

    private static string Texto(MemoryMappedViewAccessor vista, long deslocamento, int tamanho)
    {
        var bytes = new byte[tamanho];
        vista.ReadArray(deslocamento, bytes, 0, tamanho);

        var fim = Array.IndexOf(bytes, (byte)0);
        if (fim < 0) fim = tamanho;

        // o HWiNFO escreve em ANSI; Latin-1 chega para os acentos das etiquetas traduzidas
        return Encoding.Latin1.GetString(bytes, 0, fim).Trim();
    }
}
