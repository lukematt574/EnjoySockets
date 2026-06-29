// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EnjoySockets
{
    public sealed class EServer : EServer<EServerSession>
    {
        public static bool IsJIT { get; internal set; } = RuntimeFeature.IsDynamicCodeSupported;

        /// <summary>
        /// Initializes an server instance configured to accept a specified number of clients,
        /// using the provided RSA keys for secure communication and the given server configuration.
        /// </summary>
        /// <param name="rsaKey">RSA keys used for encryption and signing of connection data.</param>
        public EServer(ERSA rsaKey) : base(rsaKey, new()) { }

        /// <summary>
        /// Initializes an server instance configured to accept a specified number of clients,
        /// using the provided RSA keys for secure communication and the given server configuration.
        /// </summary>
        /// <param name="rsaKey">RSA keys used for encryption and signing of connection data.</param>
        /// <param name="config">The server configuration settings, including maximum connections, timeouts, and other behavior parameters.</param>
        public EServer(ERSA rsaKey, EConfigServer config) : base(rsaKey, config) { }
    }

    public class EServer<T1> where T1 : EServerSession
    {
        public bool Connected { get { return _servSocket?.Connected ?? false; } }
        public EndPoint? EndPointSocket { get => _servSocket?.RemoteEndPoint; }
        public AddressFamily? AddressFamilySocket { get => _servSocket?.AddressFamily; }
        public int CurrentClients { get { return _currentClients; } }
        public bool Available { get; private set; }
        public bool Listening { get; private set; }
        public bool Reconnecting { get; private set; }
        /// <summary>
        /// Start server as UTC.Ticks
        /// </summary>
        public long StartServerTimestampUTC { get; private set; }
        public EAddress? MyAddress { get; set; }
        public int ReconnectDelayMs { get; private set; } = 0;
        public Type? AuthorizationObj { get; private set; } = null;

        readonly ERSA _rsaKey;
        readonly int _delayAfterFullError;
        Socket? _servSocket;
        byte _connectResponse = 4;
        readonly byte[] _reconnectResponse = [5];
        readonly byte[] _errorFullServer = [3];
        MethodInfo? _authorizationMethod = null;
        ArrayPool<byte> _poolConnectArray = ArrayPool<byte>.Create();
        readonly ConcurrentDictionary<Guid, EServerSession> _clients = new();

        internal EConfigServer Config { get; private set; }

        /// <summary>
        /// Initializes an server instance configured to accept a specified number of clients,
        /// using the provided RSA keys for secure communication and the given server configuration.
        /// </summary>
        /// <param name="rsaKey">RSA keys used for encryption and signing of connection data.</param>
        public EServer(ERSA rsaKey) : this(rsaKey, new()) { }

        /// <summary>
        /// Initializes an server instance configured to accept a specified number of clients,
        /// using the provided RSA keys for secure communication and the given server configuration.
        /// </summary>
        /// <param name="rsaKey">RSA keys used for encryption and signing of connection data.</param>
        /// <param name="config">The server configuration settings, including maximum connections, timeouts, and other behavior parameters.</param>
        public EServer(ERSA rsaKey, EConfigServer config)
        {
            DispatcherRegistry.Initialize();
            Config = config?.Clone() ?? new EConfigServer();
            _rsaKey = rsaKey.CloneObjectToServer();
            _delayAfterFullError = Config.ResponseTimeout / 2;
            CheckUserObjAndTrySetAuth();
        }

        static readonly ConcurrentStack<ESocketResourceServer> _poolESR = new();
        static ESocketResourceServer RentESR(EConfigServer config, ERSA rsa)
        {
            if (_poolESR.TryPop(out var s))
                return s;
            else
                return new ESocketResourceServer(config, rsa);
        }

        static void ReturnESR(ESocketResourceServer? esr)
        {
            if (esr == null) return;
            esr.BasicSocket = null;
            esr.UserObj = null;
            _poolESR.Push(esr);
        }

        void CheckUserObjAndTrySetAuth()
        {
            var t = GetTypeUser();
            if ((typeof(T1) == typeof(EServerSession) || typeof(T1).IsSubclassOf(typeof(EServerSession))) && t != null)
            {
                try
                {
                    var authInterface = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEAuthorization<>));
                    if(authInterface != null)
                    {
                        Type genericArgument = authInterface.GetGenericArguments()[0];
                        MethodInfo? method = t.GetMethod("OnAuthorization", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        if (method != null)
                        {
                            var authBase = method.GetBaseDefinition();
                            if (authBase.ReturnType == typeof(Task<EConnectResult>))
                            {
                                var _authorizationParams = authBase.GetParameters();
                                if (_authorizationParams.Length == 1 && _authorizationParams[0].ParameterType == genericArgument)
                                {
                                    AuthorizationObj = genericArgument;
                                    _authorizationMethod = method;
                                    _connectResponse = 1;
                                    _reconnectResponse[0] = 2;
                                }
                            }
                        }
                    }
                }
                catch { }
                Available = true;
            }
        }

        /// <summary>
        /// Get type support user object
        /// </summary>
        Type? GetTypeUser()
        {
            var esr = RentESR(Config, _rsaKey);
            try
            {
                var obj = Activator.CreateInstance(typeof(T1), [esr]);
                return obj?.GetType();
            }
            catch
            {
                return null;
            }
            finally
            {
                ReturnESR(esr);
            }
        }

        /// <summary>
        /// Starts the TCP server and begins listening for incoming client connections.
        /// </summary>
        /// <param name="endPoint">
        /// The endpoint configuration containing the network address and port
        /// on which the server will listen.
        /// </param>
        /// <returns>
        /// <c>true</c> if the server socket was successfully created and
        /// listening has started; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// The method performs the following steps:
        /// <list type="number">
        /// <item>
        /// Validates that the server is available, not already listening,
        /// and that a valid endpoint is provided.
        /// </item>
        /// <item>
        /// Creates a TCP socket configured with:
        /// <list type="bullet">
        /// <item><description><c>ExclusiveAddressUse = true</c> (prevents other processes from binding the same port)</description></item>
        /// <item><description><c>NoDelay = true</c> (disables Nagle's algorithm for low-latency communication)</description></item>
        /// </list>
        /// </item>
        /// <item>
        /// Binds the socket to the specified endpoint and starts listening
        /// using the configured accept queue size.
        /// </item>
        /// <item>
        /// Initializes runtime state such as client counter and timestamp identifier.
        /// </item>
        /// </list>
        /// If socket initialization fails, the method safely resets the internal
        /// socket reference and returns <c>false</c>.
        /// 
        /// When successful, the listening loop is started asynchronously.
        /// </remarks>
        public bool Start(EAddress? endPoint)
        {
            ReconnectDelayMs = 0;
            return StartRun(endPoint, ReconnectDelayMs);
        }

        bool StartRun(EAddress? endPoint, int reconnectDelayMs)
        {
            if (!Available || Listening || endPoint?.EndPoint == null) return false;

            MyAddress = endPoint;

            try
            {
                _servSocket = new(endPoint.EndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
                _servSocket.Bind(endPoint.EndPoint);
                _servSocket.Listen(Config.QueueSocketToAccept);
                _currentClients = 0;
                Listening = true;
                StartServerTimestampUTC = DateTime.UtcNow.Ticks;
            }
            catch
            {
                _servSocket = null;
            }
            finally
            {
                if (_servSocket != null && Listening)
                    _ = RunListening(_servSocket);
            }
            return _servSocket != null && Listening;
        }

        /// <summary>
        /// Starts the server and enables automatic reconnection of the listening socket if it is unexpectedly closed.
        /// </summary>
        /// <param name="endPoint">The endpoint to bind the server to.</param>
        /// <param name="reconnectDelayMs">
        /// The delay, in milliseconds, between reconnection attempts. Default is 2000 ms.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the server started successfully; otherwise, <see langword="false"/>.
        /// </returns>
        public bool StartWithAutoReconnect(EAddress? endPoint, int reconnectDelayMs = 2000)
        {
            ReconnectDelayMs = reconnectDelayMs;
            return StartRun(endPoint, ReconnectDelayMs);
        }

        async Task<bool> RunStartWithReconnect(EAddress? endPoint, int reconnectAttempTime = 2000)
        {
            if (!Available || Listening || endPoint?.EndPoint == null) return false;

            ReconnectDelayMs = reconnectAttempTime;
            Reconnecting = true;
            while (Reconnecting)
            {
                if (Start(endPoint))
                    break;

                await Task.Delay(reconnectAttempTime);
            }
            return true;
        }

        /// <summary>
        /// Stops accepting new incoming client connections.
        /// </summary>
        /// <remarks>
        /// This method:
        /// <list type="bullet">
        /// <item>Disables reconnection logic.</item>
        /// <item>Stops the listening socket.</item>
        /// <item>Prevents new clients from connecting.</item>
        /// </list>
        /// Already connected clients are not disconnected.
        /// </remarks>
        public void Stop()
        {
            Reconnecting = false;
            ReconnectDelayMs = 0;
            Listening = false;
            ETCPSocket.Close(_servSocket);
            _servSocket = null;
        }

        /// <summary>
        /// Asynchronously shuts down the server and waits for all clients to disconnect.
        /// </summary>
        /// <remarks>
        /// This method performs a graceful shutdown:
        /// <list type="number">
        /// <item>Marks the server as unavailable.</item>
        /// <item>Stops accepting new connections.</item>
        /// <item>Flushes active clients.</item>
        /// <item>Waits until all clients are disconnected.</item>
        /// </list>
        /// The task completes only when the internal client collection becomes empty.
        /// </remarks>
        public async Task Dispose()
        {
            Available = false;
            Stop();
            while (true)
            {
                FlushClients();
                await Task.Delay(Config.ResponseTimeout + 5000);
                if (_clients.IsEmpty)
                    break;
            }
        }

        void FlushClients()
        {
            foreach (var client in _clients)
            {
                client.Value.Dispose();
            }
        }

        void StopAndReconnect()
        {
            Listening = false;
            ETCPSocket.Close(_servSocket);
            _servSocket = null;
            _ = RunStartWithReconnect(MyAddress, ReconnectDelayMs);
        }

        EServerSession CreateUser(ESocketResourceServer esr)
        {
            EServerSession? client = null;
            try
            {
                client = Activator.CreateInstance(typeof(T1), [esr]) as EServerSession;
            }
            catch { }
            if (client == null)
                return new EServerSession(esr);

            esr.UserObj = client;
            return client;
        }

        int _currentClients = 0;
        int _concurrentLogins = 0;
        async Task RunListening(Socket socket)
        {
            Reconnecting = false;
            while (Listening)
            {
                if (_concurrentLogins >= Config.MaxSockets)
                    await Task.Delay(50);//Throughput drops to 20 requests per second

                Socket? clientSocket = null;
                try
                {
                    clientSocket = await socket.AcceptAsync();
                    clientSocket.NoDelay = true;
                }
                catch
                {
                    Listening = false;
                    break;
                }
                if (clientSocket != null)
                {
                    Interlocked.Increment(ref _concurrentLogins);
                    _ = RunClient(clientSocket);
                }
            }
            if (ReconnectDelayMs > 0)
                StopAndReconnect();
        }

        async Task RunClient(Socket socket)
        {
            var bufferArr = _poolConnectArray.Rent(1024);
            try
            {
                var receiveTask = ReceiveConnectBytes(socket, _rsaKey, bufferArr);
                var completed = await Task.WhenAny(receiveTask, Task.Delay(Config.ResponseTimeout));

                ReadOnlyMemory<byte> connectDTO = ReadOnlyMemory<byte>.Empty;
                if (completed == receiveTask)
                    connectDTO = receiveTask.Result;

                if (connectDTO.Length > 0)
                {
                    if (
                        _clients.TryGetValue(ReadUserId(connectDTO), out EServerSession? clientAlive)
                        && clientAlive != null
                        && clientAlive.Status != EServerSessionStatus.Dead
                        )
                        await TryReconnectClient(connectDTO, socket, clientAlive);
                    else
                        await TryMakeNewClient(connectDTO, socket);

                    return;
                }
                ETCPSocket.Close(socket);
            }
            finally
            {
                _poolConnectArray.Return(bufferArr);
                Interlocked.Decrement(ref _concurrentLogins);
            }
        }

        async Task<ReadOnlyMemory<byte>> ReceiveConnectBytes(Socket? socket, ERSA rsa, byte[] bufferArr)
        {
            if (socket == null || !socket.Connected)
                return ReadOnlyMemory<byte>.Empty;

            var buffer = bufferArr.AsMemory();
            var prefix = buffer.Slice(0, ETCPSocket.PacketPrefixLength);
            if (await ETCPSocket.Read(socket, prefix))
            {
                var dataLength = BinaryPrimitives.ReadUInt16LittleEndian(prefix.Span);
                if (dataLength > 512 || dataLength < 112)
                    return ReadOnlyMemory<byte>.Empty;

                var data = buffer.Slice(0, dataLength);
                if (await ETCPSocket.Read(socket, data))
                {
                    var written = await rsa.Decrypt(data, buffer.Slice(dataLength));
                    if (written >= 112 && written <= 300)
                        return buffer.Slice(dataLength, written);
                }
            }
            return ReadOnlyMemory<byte>.Empty;
        }

        async ValueTask TryReconnectClient(ReadOnlyMemory<byte> connectDTO, Socket socket, EServerSession clientAlive)
        {
            if (clientAlive.SocketResource?.CheckReconnectToken(ReadToken(connectDTO)) ?? false)
            {
                try
                {
                    if (clientAlive.AppendSocket(socket))
                    {
                        if (await clientAlive.SocketResource.SendAsPlainBytes(_reconnectResponse))
                        {
                            clientAlive.SocketResource.SetTokenToReconnect(ReadSalt(connectDTO));
                            var msgBytes = await clientAlive.SocketResource.ReceiveEncryptWithTimeout();
                            if (msgBytes.Length > 1)
                            {
                                if (AuthorizationObj != null)
                                {
                                    var objAuth = Config.ESerial.Deserialize(msgBytes.Span, AuthorizationObj);
                                    var resAuth = await clientAlive.CheckAuthorization(objAuth);
                                    await clientAlive.SocketResource.SendBytes(resAuth.Code);
                                    if (resAuth.IsSuccess)
                                    {
                                        StartUser(clientAlive);
                                        return;
                                    }
                                }
                                else
                                {
                                    if (clientAlive.SocketResource.CheckReconnectToken(msgBytes.Span))
                                    {
                                        StartUser(clientAlive);
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
                finally
                {
                    clientAlive.TrySetBypass();
                }
            }
            ETCPSocket.Close(socket);
        }

        async ValueTask TryMakeNewClient(ReadOnlyMemory<byte> connectDTO, Socket socket)
        {
            if (ServerIsAvailable())
            {
                var esr = RentESR(Config, _rsaKey);
                if (esr.SetAesGcmKey(ReadKey(connectDTO), ReadSalt(connectDTO)))
                {
                    var signature = await esr.BuildSignature(ReadKey(connectDTO));
                    if (signature.Length > 0)
                    {
                        esr.AppendSocket(socket);
                        var bufferResponseDTO = _poolConnectArray.Rent(1024);
                        try
                        {
                            if (await esr.SendAsPlainBytes(BuildResponseDTO(_connectResponse, esr.PublicKey, signature, bufferResponseDTO)))
                            {
                                if (AuthorizationObj != null)
                                {
                                    var msgBytes = await esr.ReceiveEncryptWithTimeout();
                                    if (msgBytes.Length > 1)
                                    {
                                        var client = CreateUser(esr);
                                        client.ReleaseEvent = ReleaseUser;
                                        client.AuthorizationMethod = _authorizationMethod;
                                        var objAuth = Config.ESerial.Deserialize(msgBytes.Span, AuthorizationObj);
                                        var resAuth = await client.CheckAuthorization(objAuth);
                                        await esr.SendBytes(resAuth.Code);
                                        if (resAuth.IsSuccess)
                                        {
                                            StartUser(ReadUserId(connectDTO), client);
                                            return;
                                        }
                                    }
                                }
                                else
                                {
                                    var client = CreateUser(esr);
                                    client.ReleaseEvent = ReleaseUser;
                                    StartUser(ReadUserId(connectDTO), client);
                                    return;
                                }
                            }
                        }
                        finally
                        {
                            _poolConnectArray.Return(bufferResponseDTO);
                        }
                    }
                }
                ReturnESR(esr);
                Interlocked.Decrement(ref _currentClients);
            }
            else
            {
                if (await ETCPSocket.Send(socket, _errorFullServer))
                    await Task.Delay(_delayAfterFullError);
            }
            ETCPSocket.ShutdownAndClose(socket);
        }

        void ReleaseUser(EServerSession user, ESocketResourceServer? esr)
        {
            ReturnESR(esr);
            _clients.TryRemove(user.UserId, out _);
            Interlocked.Decrement(ref _currentClients);
        }

        bool ServerIsAvailable()
        {
            while (true)
            {
                int current = _currentClients;

                if (current >= Config.MaxSockets)
                    return false;

                int result = Interlocked.CompareExchange(
                    ref _currentClients,
                    current + 1,
                    current
                );

                if (result == current)
                    return true;
            }
        }

        void StartUser(EServerSession user)
        {
            if (!user.Start())
                user.Dispose();
        }

        void StartUser(Guid id, EServerSession user)
        {
            user.UserId = id;
            if (!(user.Start() && _clients.TryAdd(id, user)))
                user.Dispose();
        }

        Guid ReadUserId(ReadOnlyMemory<byte> buffer) => new(buffer.Span[..16]);
        ReadOnlySpan<byte> ReadToken(ReadOnlyMemory<byte> buffer) => buffer.Slice(16, 32).Span;
        ReadOnlySpan<byte> ReadSalt(ReadOnlyMemory<byte> buffer) => buffer.Slice(48, 32).Span;
        ReadOnlySpan<byte> ReadKey(ReadOnlyMemory<byte> buffer) => buffer[80..].Span;
        ReadOnlyMemory<byte> BuildResponseDTO(byte control, ReadOnlyMemory<byte> publicKey, ReadOnlyMemory<byte> signature, byte[] buffer)
        {
            var length = 5 + publicKey.Length + signature.Length;
            if (length <= buffer.Length)
            {
                buffer[0] = control;
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(1, 2), (ushort)publicKey.Length);
                BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(3, 2), (ushort)signature.Length);
                publicKey.CopyTo(buffer.AsMemory(5));
                signature.CopyTo(buffer.AsMemory(5 + publicKey.Length));
                return new ReadOnlyMemory<byte>(buffer, 0, length);
            }
            return ReadOnlyMemory<byte>.Empty;
        }
    }
}
