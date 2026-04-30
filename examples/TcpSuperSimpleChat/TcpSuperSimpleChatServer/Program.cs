using EnjoySockets;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

string PrivatePemKey = "-----BEGIN PRIVATE KEY-----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-----END PRIVATE KEY-----";
string PrivatePemKeyToSign = "-----BEGIN PRIVATE KEY-----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-----END PRIVATE KEY-----";

var serv = new ETCPServer<MyUserServer>(new ERSA(PrivatePemKey, PrivatePemKeyToSign));
var addr = EAddress.Get();
var started = serv.Start(addr);

if (started)
    Console.WriteLine($"Server started on {addr.EndPoint?.ToString()}");
else
    Console.WriteLine("Failed to start the server");
Console.ReadKey();

public class MyUserServer : EUserServer
{
    static int _idValue;
    public int UserId { get; private set; }

    public MyUserServer(ESocketResourceServer ers) : base(ers) { }

    protected override void OnConnected()
    {
        UserId = Interlocked.Increment(ref _idValue);
    }
}

[EAttr(ChannelId = ChatSync)]
public static class MainChat
{
    [EAttrChannel(ChannelType = EChannelType.Share, ChannelTasks = 1)]
    public const ushort ChatSync = 1;

    [EAttrPool(MaxPoolObjs = 1000)]
    public const ushort MsgPool = 1;

    static List<byte> _message = [0, 0, 0, 0];
    static readonly EArrayBufferWriter _bufferMsg = new(5000);
    static readonly List<MyUserServer> _users = [];

    static long RegisterUser(MyUserServer user)
    {
        if (!_users.Contains(user))
        {
            _users.Add(user);
            return user.UserId;
        }
        return 0;
    }

    [EAttr(PoolId = MsgPool, MaxParamSize = 4096)]
    static void PushMsg(MyUserServer user, List<byte> msg)
    {
        _message.RemoveRange(4, _message.Count - 4);
        BinaryPrimitives.WriteInt32LittleEndian(CollectionsMarshal.AsSpan(_message), user.UserId);
        _message.AddRange(msg);

        if (ESerial.Serialize(_bufferMsg, _message) < 1)
            return;

        var payload = _bufferMsg.WrittenSpan;
        for (int i = 0; i < _users.Count; i++)
        {
            var u = _users[i];
            if (!u.SendSerialized("PushMsg", payload) && u.Status == ESocketServerStatus.Dead)
            {
                _users.RemoveAt(i);
                i--;
            }
        }
    }
}