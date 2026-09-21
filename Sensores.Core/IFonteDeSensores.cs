namespace Sensores.Core;

/// <summary>
/// De onde vêm os valores. Há uma implementação por sistema operativo: em Windows a
/// leitura passa por bibliotecas e drivers; em Linux o kernel publica quase tudo em
/// ficheiros de texto, e não é preciso nada disso.
/// </summary>
internal interface IFonteDeSensores : IDisposable
{
    Leitura Ler();

    /// <summary>Tudo o que a fonte consegue ver, para se confirmar de onde vem cada número.</summary>
    IEnumerable<(string Hardware, string Tipo, string Sensor, float? Valor)> Inventario();
}
