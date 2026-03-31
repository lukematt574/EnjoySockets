// Copyright (c) Luke Matt. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
using EnjoySockets.DTO;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace EnjoySockets
{
    public sealed class ETCPServer : ETCPServer<EUserServer>
    {
        public static bool IsJIT { get; internal set; } = RuntimeFeature.IsDynamicCodeSupported;

        /// <summary>
        /// Initializes an server instance configured to accept a specified number of clients,
        /// using the provided RSA keys for secure communication and the given server configuration.
        /// </summary>
        /// <param name="rsaKey">RSA keys used for encryption and signing of connection data.</param>
        public ETCPServer(ERSA rsaKey) : base(rsaKey, new()) { }

        /// <summary>
        /// Initializes an server instance configured to accept a specified number of clients,
        /// using the provided RSA keys for secure communication and the given server configuration.
        /// </summary>
        /// <param name="rsaKey">RSA keys used for encryption and signing of connection data.</param>
        /// <param name="config">The server configuration settings, including maximum connections, timeouts, and other behavior parameters.</param>
        public ETCPServer(ERSA rsaKey, ETCPServerConfig config) : base(rsaKey, config) { }
    }

    public class ETCPServer<T1> where T1 : EUserServer
    {
        public bool Connected { get { return _servSocket?.Connected ?? false; } }
        public EndPoint? EndPointSocket { get => _servSocket?.RemoteEndPoint; }
        public AddressFamily? AddressFamilySocket { get => _servSocket?.AddressFamily; }
        public int CurrentClients { get { return _currentClients; } }
        public bool Available { get; private set; }
        public bool Listening { get; private set; }
        public bool Reconnecting { get; private set; }
        /// <summary>
        /// Start serwer in UtcNow.Ticks
        /// </summary>
        public long TimestampID { get; private set; }
        public EAddress? MyAddress { get; set; }
        public int ReconnectDelayMs { get; private set; } = 0;
        public Type? AuthorizationObj { get; private set; } = null;

        readonly ERSA _rsaKey;
        Socket? _servSocket;
        MethodInfo? _authorizationMethod = null;

        internal ETCPServerConfig Config { get; private set; }

        /// <summary>
        /// Initializes an server instance configured to accept a specified number of clients,
        /// using the provided RSA keys for secure communication and the given server configuration.
        /// </summary>
        /// <param name="rsaKey">RSA keys used for encryption and signing of connection data.</param>
        public ETCPServer(ERSA rsaKey) : this(rsaKey, new()) { }

        /// <summary>
        /// Initializes an server instance configured to accept a specified number of clients,
        /// using the provided RSA keys for secure communication and the given server configuration.
        /// </summary>
        /// <param name="rsaKey">RSA keys used for encryption and signing of connection data.</param>
        /// <param name="config">The server configuration settings, including maximum connections, timeouts, and other behavior parameters.</param>
        public ETCPServer(ERSA rsaKey, ETCPServerConfig config)
        {
            EReceiveCells.Initialize();
            Config = config?.Clone() ?? new ETCPServerConfig();
            _rsaKey = rsaKey.CloneObjectToServer();
            CheckUserObjAndTrySetAuth();
        }

        static readonly ConcurrentStack<ESocketResourceServer> _poolESR = new();
        static ESocketResourceServer RentESR(ETCPServerConfig config, ERSA rsa)
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
            if ((typeof(T1) == typeof(EUserServer) || typeof(T1).IsSubclassOf(typeof(EUserServer))) && CheckTypeUser())
            {
                try
                {
                    MethodInfo? method = typeof(T1).GetMethod("Authorization", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (method != null)
                    {
                        var authBase = method.GetBaseDefinition();
                        if (authBase.ReturnType == typeof(Task<byte>))
                        {
                            var _authorizationParams = authBase.GetParameters();
                            if (_authorizationParams.Length == 1)
                            {
                                AuthorizationObj = _authorizationParams[0].ParameterType;
                                _authorizationMethod = method;
                            }
                        }
                    }
                }
                catch { }
                Available = true;
            }
        }

        /// <summary>
        /// Check type support user object
        /// </summary>
        /// <returns>false - no support, true - support</returns>
        bool CheckTypeUser()
        {
            var esr = RentESR(Config, _rsaKey);
            try
            {
                _ = Activator.CreateInstance(typeof(T1), [esr]) as EUserServer;
                return true;
            }
            catch
            {
                return false;
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
                TimestampID = DateTime.UtcNow.Ticks;
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

        EUserServer CreateUser(ESocketResourceServer esr)
        {
            EUserServer? client = null;
            try
            {
                client = Activator.CreateInstance(typeof(T1), [esr]) as EUserServer;
            }
            catch { }
            if (client == null)
                return new EUserServer(esr);

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

        readonly byte[] _errorFullServer = ESerial.Serialize(new ConnectResponseDTO() { Control = 3 }) ?? [];
        readonly ConcurrentDictionary<Guid, EUserServer> _clients = new();
        async Task RunClient(Socket socket)
        {
            var receiveTask = ETCPSocket.Receive(socket, _rsaKey);
            var completed = await Task.WhenAny(receiveTask, Task.Delay(Config.ResponseTimeout));

            ConnectDTO? connectDTO = null;
            if (completed == receiveTask)
                connectDTO = receiveTask.Result;

            if (connectDTO != null)
            {
                if (
                    _clients.TryGetValue(connectDTO.UserId, out EUserServer? clientAlive)
                    && clientAlive != null
                    && clientAlive.Status != ESocketServerStatus.Dead
                    )
                    await TryReconnectClient(connectDTO, socket, clientAlive);
                else
                    await TryMakeNewClient(connectDTO, socket);

                ETCPSocket.ReturnConnectDTO(connectDTO);
                return;
            }
            ETCPSocket.Close(socket);
            Interlocked.Decrement(ref _concurrentLogins);
        }

        async ValueTask TryReconnectClient(ConnectDTO connectDTO, Socket socket, EUserServer clientAlive)
        {
            if (clientAlive.SocketResource?.TryReconnectToken(CollectionsMarshal.AsSpan(connectDTO.TokenToReconnect), CollectionsMarshal.AsSpan(connectDTO.NewTokenToReconnect)) ?? false)
            {
                try
                {
                    if (clientAlive.AppendSocket(socket))
                    {
                        if (await clientAlive.SocketResource.SendPlainBytesObj(new ConnectResponseDTO() { Control = AuthorizationObj != null ? (byte)1 : (byte)4 }))
                        {
                            if (AuthorizationObj != null)
                            {
                                var msgBytes = await clientAlive.SocketResource.ReceiveEncryptWithTimeout();
                                if (msgBytes.Length > 1)
                                {
                                    var objAuth = ESerial.Deserialize(msgBytes.Span, AuthorizationObj);
                                    var resAuth = await clientAlive.CheckAuthorization(objAuth);
                                    await clientAlive.SocketResource.SendBytes(resAuth);
                                    if (resAuth == 0)
                                    {
                                        StartUser(clientAlive);
                                        return;
                                    }
                                }
                            }
                            else
                            {
                                StartUser(clientAlive);
                                return;
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
            Interlocked.Decrement(ref _concurrentLogins);
        }

        async ValueTask TryMakeNewClient(ConnectDTO connectDTO, Socket socket)
        {
            if (ServerIsAvailable())
            {
                var esr = RentESR(Config, _rsaKey);

                if (esr.SetAesGcmKey(CollectionsMarshal.AsSpan(connectDTO.Key), CollectionsMarshal.AsSpan(connectDTO.NewTokenToReconnect)))
                {
                    var signature = await esr.BuildSignature(connectDTO);
                    if (signature.Length > 0)
                    {
                        esr.AppendSocket(socket);
                        if (await esr.SendPlainBytesObj(new ConnectResponseDTO() { Control = AuthorizationObj != null ? (byte)2 : (byte)5, PublicKey = esr.PublicKey, Sign = signature }))
                        {
                            if (AuthorizationObj != null)
                            {
                                var msgBytes = await esr.ReceiveEncryptWithTimeout();
                                if (msgBytes.Length > 1)
                                {
                                    var client = CreateUser(esr);
                                    client.ReleaseEvent = ReleaseUser;
                                    client.AuthorizationMethod = _authorizationMethod;
                                    var objAuth = ESerial.Deserialize(msgBytes.Span, AuthorizationObj);
                                    var resAuth = await client.CheckAuthorization(objAuth);
                                    await esr.SendBytes(resAuth);
                                    if (resAuth == 0)
                                    {
                                        StartUser(connectDTO.UserId, client);
                                        return;
                                    }
                                }
                            }
                            else
                            {
                                var client = CreateUser(esr);
                                client.ReleaseEvent = ReleaseUser;
                                StartUser(connectDTO.UserId, client);
                                return;
                            }
                        }
                    }
                }
                ReturnESR(esr);
                Interlocked.Decrement(ref _currentClients);
            }
            else
            {
                await ETCPSocket.Send(socket, _errorFullServer);
            }
            ETCPSocket.ShutdownAndClose(socket);
            Interlocked.Decrement(ref _concurrentLogins);
        }

        void ReleaseUser(EUserServer user, ESocketResourceServer? esr)
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

        void StartUser(EUserServer user)
        {
            if (!user.Start())
                user.Dispose();

            Interlocked.Decrement(ref _concurrentLogins);
        }

        void StartUser(Guid id, EUserServer user)
        {
            user.UserId = id;
            if (!(user.Start() && _clients.TryAdd(id, user)))
                user.Dispose();

            Interlocked.Decrement(ref _concurrentLogins);
        }
    }
}
