namespace Sensores.Core;

/// <summary>
/// O leitor de sensores. Escolhe sozinho a fonte conforme o sistema operativo: em Windows
/// passa por bibliotecas e drivers, em Linux lê os ficheiros que o kernel publica.
///
/// Quem usa isto não precisa de saber a diferença: a <see cref="Leitura"/> é a mesma, e o
/// que não existir num sistema vem a null, como qualquer outro sensor em falta.
/// </summary>
public sealed class Leitor : IDisposable
{
    private readonly IFonteDeSensores _fonte;
    private bool _fechado;

    public Leitor()
    {
        _fonte = OperatingSystem.IsWindows() ? new FonteWindows() : new FonteLinux();
    }

    /// <summary>Nome da fonte em uso, para diagnóstico.</summary>
    public string Fonte => OperatingSystem.IsWindows() ? "Windows" : "Linux";

    public Leitura Ler() => _fonte.Ler();

    /// <summary>Tudo o que a fonte vê, com os nomes reais dos sensores.</summary>
    public IEnumerable<(string Hardware, string Tipo, string Sensor, float? Valor)> Inventario() =>
        _fonte.Inventario();

    public void Dispose()
    {
        if (_fechado) return;
        _fechado = true;
        _fonte.Dispose();
    }
}
