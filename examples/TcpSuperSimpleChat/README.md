# Minimal Chat Example

This chat is designed as the most minimal working example possible.  
Both the client and server run in the console.

The goal was to demonstrate the basics of using the library with as little code as possible.  
Despite its small size, this implementation provides a fully functional global chat with:

- backpressure handling  
- zero allocations (zero alloc)  
- controlled data flow  

---

## Server Side

Since this is a global chat shared by all users, only static methods are used, along with channel synchronization configured as:

```csharp
[EAttrChannel(ChannelType = EChannelType.Share, ChannelTasks = 1)]
````

To distinguish users in the simplest possible way, a custom user class is created by extending the base user class:

```csharp
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
```

Then the server is initialized using the custom user class:

```csharp
var serv = new ETCPServer<MyUserServer>(new ERSA(PrivatePemKey, PrivatePemKeyToSign));
```

The main logic is implemented in the static `MainChat.cs` class.
At startup, a buffer is allocated for message processing.

To avoid creating a dedicated DTO object, the first 4 bytes of the message are used to store the user ID (`int`), followed by the actual message content.
This allows sending messages conveniently as `List<byte>`.

A `string` input parameter is intentionally avoided to prevent per-message memory allocations.

The `SendSerialized` method is used for sending messages because it always completes synchronously.
Dead users are removed inline during message broadcasting.

A pool of `List<byte>` objects is used, limited to a maximum of 1000 instances.
Any excess objects are handled by the GC, making it easy to control peak memory usage and avoid excessive allocations during traffic spikes.
In this case, it is easy to calculate that the pool can use at most 4 MB of RAM (1000 × 4096), unless it is used by another endpoint with the same type and without a `MaxParamSize` limit.

Everything is coordinated through the `ChatSync` channel, which operates on a single task handling all socket communication.

### MainChat.cs

```csharp
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
```

---

## Client Side

After connecting and registering on the server, the client can immediately start using the chat.

The client runs an infinite loop reading input from the console and sending messages.
It also defines a `PushMsg` handler that receives and prints messages from the server.

```csharp
var client = new EUserClient(new ERSA(PublicPemKey, PublicPemKeyToSign));
if (await client.Connect(EAddress.Get()) == 0)
{
    long id = await client.SendWithResponse("RegisterUser");
    if (id > 0)
    {
        Console.WriteLine($"You have joined the chat room as User({id})");
        while (true)
        {
            var message = Console.ReadLine();

            var bytesMsg = Encoding.UTF8.GetBytes(message ?? "").ToList();
            if (bytesMsg.Count > 4000 || bytesMsg.Count < 1)
            {
                Console.WriteLine("Message length is out of range");
                continue;
            }

            if (!await client.Send("PushMsg", bytesMsg))
            {
                Console.WriteLine("Failed to send message");
                continue;
            }
        }
    }
    else
    {
        Console.WriteLine("Failed to join the room");
        Environment.Exit(0);
    }
}
else
{
    Console.WriteLine("Failed to connect to the server");
    Environment.Exit(0);
}
```

### Message Receiver

```csharp
public static class MainChat
{
    [EAttr(PoolId = 1, MaxParamSize = 4096)]
    static void PushMsg(EUserClient user, List<byte> msg)
    {
        if (msg.Count <= 4)
            return;

        var spanMsg = CollectionsMarshal.AsSpan(msg);

        int userId = BinaryPrimitives.ReadInt32LittleEndian(spanMsg[..4]);
        string message = Encoding.UTF8.GetString(spanMsg[4..]);
        Console.WriteLine($"User({userId}): {message}");
    }
}
```