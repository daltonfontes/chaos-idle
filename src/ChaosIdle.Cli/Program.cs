using System.Globalization;
using ChaosIdle.Cli;

// Usage:
//   dotnet run --project src/ChaosIdle.Cli -- compare [runs=200] [firstSeed=1]
//   dotnet run --project src/ChaosIdle.Cli -- expedition [seed=1] [patrols=8]
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = System.Text.Encoding.UTF8;

string command = args.Length > 0 ? args[0] : "expedition";
var rest = args.Skip(1).ToArray();
switch (command)
{
    case "compare":
        CompareCommand.Run(rest);
        break;
    case "expedition":
        ExpeditionCommand.Run(rest);
        break;
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Use 'compare' or 'expedition'.");
        return 1;
}

return 0;
