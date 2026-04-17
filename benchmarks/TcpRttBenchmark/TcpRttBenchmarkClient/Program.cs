// See https://aka.ms/new-console-template for more information

using TcpRttBenchmarkClient;
using TcpRttBenchmarkClient.ES;
using TcpRttBenchmarkClient.SS;
using TcpRttBenchmarkClient.WTCP;

Console.WriteLine("Performance test (client)\n");
Console.WriteLine("Set server IP");
GlobalConfig.IP = Console.ReadLine() ?? "";
Console.WriteLine("Set server Port");
GlobalConfig.Port = int.TryParse(Console.ReadLine(), out int port) ? port : 3001;

Console.WriteLine("\nChoose library:");
Console.WriteLine("1 - EnjoySockets");
Console.WriteLine("2 - SuperSocket");
Console.WriteLine("3 - WatsonTCP");

switch (Console.ReadLine())
{
    case "1":
        _ = new EnjoySocketsClass().StartTest();
        break;
    case "2":
        _ = new SuperSocketClass().StartTest();
        break;
    case "3":
        _ = new WatsonTCPClass().StartTest();
        break;
    default:
        Console.WriteLine("Wrong library id");
        break;
}
Console.ReadKey();