using System.Globalization;
using System.Text;
using System.Text.Json;
using Sensores.Cli;
using Sensores.Core;

Console.OutputEncoding = Encoding.UTF8;

var argumentos = args.Select(a => a.ToLowerInvariant()).ToHashSet();

if (argumentos.Contains("--ajuda") || argumentos.Contains("-h") || argumentos.Contains("--help"))
{
    Console.WriteLine($"""
        {Marca.Assinatura}

          vitals                uma leitura e sai
          vitals --watch        fica a actualizar (ctrl+c para sair)
          vitals --json         uma leitura em JSON
          vitals --sensores     todos os sensores detectados, com os nomes reais
          vitals --help         esta ajuda (tambem --ajuda ou -h)

        Opcoes:
          --intervalo <seg>    periodo do --watch (por omissao 1)

        O widget e um programa separado: Vitals.exe (janela junto ao relogio).
        """);
    return 0;
}

using var leitor = new Leitor();

if (argumentos.Contains("--sensores"))
{
    Console.WriteLine($"{Marca.Assinatura}\n");
    string? anterior = null;
    foreach (var (hardware, tipo, sensor, valor) in leitor.Inventario())
    {
        if (hardware != anterior) { Console.WriteLine($"\n{hardware}"); anterior = hardware; }
        var v = valor.HasValue ? valor.Value.ToString("F1", CultureInfo.InvariantCulture) : "sem valor";
        Console.WriteLine($"  {tipo,-12} {sensor,-34} {v}");
    }
    return 0;
}

if (argumentos.Contains("--hwinfo"))
{
    Console.WriteLine($"{Marca.Assinatura}\n");

    var daMemoria = HwInfoMemoria.Ler();
    if (daMemoria.Count > 0)
    {
        Console.WriteLine($"memoria partilhada do HWiNFO: {daMemoria.Count} leituras\n");
        string? anteriorSensor = null;
        foreach (var leitura in daMemoria)
        {
            if (leitura.Sensor != anteriorSensor) { Console.WriteLine($"\n{leitura.Sensor}"); anteriorSensor = leitura.Sensor; }
            Console.WriteLine($"  {leitura.Etiqueta,-44} {leitura.Valor,10:F1} {leitura.Unidade}");
        }
        return 0;
    }

    Console.WriteLine("memoria partilhada indisponivel (HWiNFO fechado, opcao desligada, ou passadas as 12 horas)");

    var doRegisto = HwInfo.Ler();
    Console.WriteLine(doRegisto.Count > 0
        ? $"\nregisto (gadget): {doRegisto.Count} valores"
        : "\nregisto (gadget): nada publicado");
    foreach (var p in doRegisto) Console.WriteLine($"  {p.Etiqueta,-44} {p.Valor,10:F1}");
    return 0;
}

if (argumentos.Contains("--json"))
{
    Thread.Sleep(600);   // segunda amostra: as percentagens de carga precisam de delta
    var leitura = leitor.Ler();
    Console.WriteLine(JsonSerializer.Serialize(leitura, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

var intervalo = 1.0;
var iIntervalo = Array.FindIndex(args, a => a.Equals("--intervalo", StringComparison.OrdinalIgnoreCase));
if (iIntervalo >= 0 && iIntervalo + 1 < args.Length)
    double.TryParse(args[iIntervalo + 1], CultureInfo.InvariantCulture, out intervalo);

Apresentacao.Preparar();

if (!argumentos.Contains("--watch") && !argumentos.Contains("-w"))
{
    Thread.Sleep(600);
    Console.WriteLine();
    Apresentacao.Desenhar(leitor.Ler(), limpar: false);
    return 0;
}

using var cancelamento = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelamento.Cancel(); };

try
{
    Apresentacao.AbrirEcra();

    var espera = TimeSpan.FromSeconds(Math.Clamp(intervalo, 0.2, 60));

    while (!cancelamento.IsCancellationRequested)
    {
        Apresentacao.Desenhar(leitor.Ler(), limpar: true);

        // esperar pela próxima leitura, mas sem ficar surdo: as teclas de rolagem têm de
        // responder logo, e não só quando o intervalo acabar
        var proxima = DateTime.UtcNow + espera;
        while (DateTime.UtcNow < proxima && !cancelamento.IsCancellationRequested)
        {
            if (Teclas.Ha() && Teclas.Tratar(Console.ReadKey(intercept: true)) is { } accao)
            {
                if (accao == Teclas.Accao.Sair) { cancelamento.Cancel(); break; }
                if (accao == Teclas.Accao.Redesenhar) break;   // rolou: mostrar já
            }

            await Task.Delay(40, cancelamento.Token);
        }
    }
}
catch (OperationCanceledException) { }
finally
{
    Apresentacao.FecharEcra();
    Console.ResetColor();
}
return 0;
