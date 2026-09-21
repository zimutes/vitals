namespace Sensores.Cli;

/// <summary>
/// Teclado do modo <c>--watch</c>: rolar o painel quando ele não cabe na janela, e sair.
///
/// Só faz sentido com terminal de verdade. Quando a entrada vem redireccionada — um
/// <c>ssh comando</c> sem TTY, ou a saída a ser canalizada — não há teclas para ler, e
/// perguntar por elas dá excepção.
/// </summary>
public static class Teclas
{
    public enum Accao
    {
        Redesenhar,
        Sair,
    }

    public static bool Ha()
    {
        try
        {
            return !Console.IsInputRedirected && Console.KeyAvailable;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Trata a tecla. Devolve null quando não é nenhuma das nossas.</summary>
    public static Accao? Tratar(ConsoleKeyInfo tecla)
    {
        switch (tecla.Key)
        {
            case ConsoleKey.Q or ConsoleKey.Escape:
                return Accao.Sair;

            case ConsoleKey.UpArrow:
                return Apresentacao.Rolar(-1) ? Accao.Redesenhar : null;

            case ConsoleKey.DownArrow:
                return Apresentacao.Rolar(1) ? Accao.Redesenhar : null;

            case ConsoleKey.PageUp:
                return Apresentacao.Rolar(-10) ? Accao.Redesenhar : null;

            case ConsoleKey.PageDown:
                return Apresentacao.Rolar(10) ? Accao.Redesenhar : null;

            case ConsoleKey.Home:
                return Apresentacao.RolarParaOTopo() ? Accao.Redesenhar : null;

            case ConsoleKey.End:
                return Apresentacao.RolarParaOFim() ? Accao.Redesenhar : null;
        }

        // k e j, como no less e no vim
        return tecla.KeyChar switch
        {
            'k' => Apresentacao.Rolar(-1) ? Accao.Redesenhar : null,
            'j' => Apresentacao.Rolar(1) ? Accao.Redesenhar : null,
            _ => null,
        };
    }
}
