using EnjoySockets;
using SuperSocket.ProtoBase;
using SuperSocket.Server.Abstractions;
using SuperSocket.Server.Abstractions.Session;
using SuperSocket.Server.Host;
using System.Buffers;

namespace TcpRttBenchmarkServer
{
    public class MyPackage
    {
        public ReadOnlySequence<byte> Data { get; set; }
    }

    public class MyFixedSizePipelineFilter : FixedSizePipelineFilter<MyPackage>
    {
        private readonly MyPackage _package = new();

        public MyFixedSizePipelineFilter() : base(1028) { }

        protected override MyPackage DecodePackage(ref ReadOnlySequence<byte> buffer)
        {
            _package.Data = buffer;

            buffer = buffer.Slice(buffer.End);

            return _package;
        }
    }

    public class SuperSocketClass
    {
        Type _typeArg;
        EObjPool _objPool;

        public SuperSocketClass()
        {
            _typeArg = typeof(List<long>);
            _objPool = new EObjPool(_typeArg, 0);
        }

        public async Task CreateServer()
        {
            var builder = SuperSocketHostBuilder.Create<MyPackage, MyFixedSizePipelineFilter>();
            builder
                .UsePackageHandler(Receive)
                .ConfigureSuperSocket(options =>
                {
                    options.Name = "TestServer";
                    options.Listeners = [new()
                    {
                        Ip = GlobalConfig.IP,
                        Port = GlobalConfig.Port,
                        NoDelay = true,
                        AuthenticationOptions = new ServerAuthenticationOptions()
                        {
                          CertificateOptions = new CertificateOptions(){ FilePath = "cert.pfx", Password = "password" },
                          EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12
                        }
                    }];
                });

            var host = builder.Build();
            await host.StartAsync();
        }

        byte[] response = [1];
        async ValueTask Receive(IAppSession session, MyPackage package)
        {
            var obj = _objPool.Rent();
            if (ESerial.Deserialize(package.Data, _typeArg, ref obj))
            {
                var list = obj as List<long>;
                if (list != null)
                {
                    await session.SendAsync(response);
                    _objPool.Return(obj);
                }
            }
        }
    }
}
