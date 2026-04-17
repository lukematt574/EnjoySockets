// See https://aka.ms/new-console-template for more information

using TcpRttBenchmarkServer;

Console.WriteLine("Performance test (server)\n");
Console.WriteLine("Set server listener IP");
GlobalConfig.IP = Console.ReadLine() ?? "";
Console.WriteLine("Set server listener Port");
GlobalConfig.Port = int.TryParse(Console.ReadLine(), out int port) ? port : 3001;

Console.WriteLine("\nChoose library:");
Console.WriteLine("1 - EnjoySockets");
Console.WriteLine("2 - SuperSocket");
Console.WriteLine("3 - WatsonTCP");

switch (Console.ReadLine())
{
    case "1":
        new EnjoySocketsClass().CreateServer();
        break;
    case "2":
        await new SuperSocketClass().CreateServer();
        break;
    case "3":
        new WatsonTCPClass().CreateServer();
        break;
    default:
        Console.WriteLine("Wrong library id");
        break;
}
Console.ReadKey();