namespace Sensores.Core;

/// <summary>Identidade da aplicacao. Mudar o nome aqui muda-o em todo o sitio.</summary>
public static class Marca
{
    public const string Nome = "Vitals";
    public const string Autor = "by zimutes";
    public const string Versao = "1.1";

    public static string Assinatura => $"{Nome} {Versao} · {Autor}";
}

