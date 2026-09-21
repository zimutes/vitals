using System.Runtime.InteropServices;

namespace Sensores.Core;

/// <summary>
/// Limites térmicos reais da placa, lidos uma vez à NVML (nvml.dll do controlador).
///
/// A NVIDIA deixou de expor o hotspot absoluto nas GeForce recentes; o que publica é o
/// "T.Limit", a margem até ao limite. Verificado nesta máquina: com o core a 48 °C o
/// nvidia-smi devolvia T.Limit 37, e 85 - 48 = 37. Ou seja, basta saber o limite de
/// operação para calcular a margem.
///
/// Se a leitura falhar (sem placa NVIDIA, sem controlador, DLL ausente), fica tudo a null
/// e quem usa recorre aos valores por omissão.
/// </summary>
public sealed class LimitesNvidia
{
    private delegate int SemArgumentos();
    private delegate int ObterDispositivo(uint indice, out IntPtr dispositivo);
    private delegate int ObterLimiar(IntPtr dispositivo, int tipo, out uint graus);

    private const int Shutdown_ = 0;
    private const int Slowdown_ = 1;
    private const int MaxOperacional_ = 3;

    /// <summary>Temperatura a que a placa começa a reduzir desempenho.</summary>
    public float? MaxOperacional { get; }
    public float? Slowdown { get; }
    public float? Shutdown { get; }

    public bool Disponivel => MaxOperacional is not null;

    public LimitesNvidia()
    {
        IntPtr biblioteca = IntPtr.Zero;
        try
        {
            // a mesma NVML tem nomes diferentes conforme o sistema
            var candidatos = OperatingSystem.IsWindows()
                ? new[] { "nvml.dll", @"C:\Windows\System32\nvml.dll" }
                : new[] { "libnvidia-ml.so.1", "libnvidia-ml.so" };

            IntPtr carregada = IntPtr.Zero;
            if (!candidatos.Any(nome => NativeLibrary.TryLoad(nome, out carregada))) return;
            biblioteca = carregada;

            var iniciar = Ligar<SemArgumentos>(biblioteca, "nvmlInit_v2");
            var terminar = Ligar<SemArgumentos>(biblioteca, "nvmlShutdown");
            var dispositivo = Ligar<ObterDispositivo>(biblioteca, "nvmlDeviceGetHandleByIndex_v2");
            var limiar = Ligar<ObterLimiar>(biblioteca, "nvmlDeviceGetTemperatureThreshold");

            if (iniciar is null || terminar is null || dispositivo is null || limiar is null) return;
            if (iniciar() != 0) return;

            try
            {
                if (dispositivo(0, out var gpu) != 0) return;

                MaxOperacional = Limite(limiar, gpu, MaxOperacional_);
                Slowdown = Limite(limiar, gpu, Slowdown_);
                Shutdown = Limite(limiar, gpu, Shutdown_);
            }
            finally
            {
                terminar();   // a NVML conta referências: não afecta quem mais a esteja a usar
            }
        }
        catch (Exception)
        {
            // sem limites: o widget usa os valores por omissão
        }
        finally
        {
            if (biblioteca != IntPtr.Zero) NativeLibrary.Free(biblioteca);
        }
    }

    private static float? Limite(ObterLimiar limiar, IntPtr gpu, int tipo) =>
        limiar(gpu, tipo, out var graus) == 0 && graus is > 0 and < 150 ? graus : null;

    private static T? Ligar<T>(IntPtr biblioteca, string nome) where T : Delegate =>
        NativeLibrary.TryGetExport(biblioteca, nome, out var ponteiro)
            ? Marshal.GetDelegateForFunctionPointer<T>(ponteiro)
            : null;
}
