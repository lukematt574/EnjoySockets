# EnjoySockets
[![NuGet Version](https://img.shields.io/nuget/v/EnjoySockets)](https://www.nuget.org/packages/EnjoySockets/)

**EnjoySockets** is a high-performance C# library designed for TCP communication, with UDP support currently on the roadmap.

## Why EnjoySockets?

While working on numerous client-server projects, I often found myself lacking a "batteries-included" library that provided the necessary functionality out of the box. Many existing .NET libraries fall into two categories:
1. **Too low-level:** Simple at first glance, but requiring you to manually implement critical components like flow control, pooling, and endpoint management.
2. **Too complex:** Highly performant, but with a steep learning curve and a requirement to maintain significant amounts of boilerplate code throughout the project's lifecycle.

**EnjoySockets** bridges this gap by offering a comprehensive, performance-first suite of tools for socket programming.

## Key Features

* 🚀 **Performance:** Zero-allocation focus, full `async` support, and optimized binary serialization.
* 🛠️ **Ease of Use:** High-level abstractions including RPC, built-in session reconnection, and instance management.
* ⚖️ **Resource Control:** Robust flow control using `System.Threading.Channels` and object pooling.
* 🔌 **Non-invasive:** Designed for easy adoption into existing codebases without major refactoring.

## Contents

**Basics**

* [Serialization](#serialization) &nbsp;&nbsp;|&nbsp;&nbsp; [Installation](#installation) &nbsp;&nbsp;|&nbsp;&nbsp; [Compatibility & Performance](#compatibility--performance) &nbsp;&nbsp;|&nbsp;&nbsp; [Native AOT Configuration](#native-aot-configuration) &nbsp;&nbsp;|&nbsp;&nbsp; [Quick Start](#quick-start)

---

**Doc**

* [Encryption & Security](#-encryption--security) &nbsp;&nbsp;|&nbsp;&nbsp; [Customizing User Classes](#customizing-user-classes) &nbsp;&nbsp;|&nbsp;&nbsp; [Connection Management](#-connection-management) &nbsp;&nbsp;|&nbsp;&nbsp; [Sending Messages](#-sending-messages) &nbsp;&nbsp;|&nbsp;&nbsp; [Receiving Messages](#-receiving-messages) &nbsp;&nbsp;|&nbsp;&nbsp; [Instance Architecture](#-instance-architecture) &nbsp;&nbsp;|&nbsp;&nbsp; [Object Pooling](#object-pooling) &nbsp;&nbsp;|&nbsp;&nbsp; [Flow Control Channels](#-flow-control-channels)

---

**Other**

* [Multicasting](#-multicasting) &nbsp;&nbsp;|&nbsp;&nbsp; [Scalability & Distributed Systems](#-scalability--distributed-systems)


## Serialization

The library leverages the **MemoryPack** engine by [neuecc](https://github.com/neuecc) to ensure the most efficient serialization possible. 
> [!IMPORTANT]
> For advanced serialization scenarios and proper attribute usage, please refer to the [MemoryPack documentation](https://github.com/Cysharp/MemoryPack).

## Installation

The library is available via NuGet:

```powershell
Install-Package EnjoySockets

```

### Compatibility & Performance

EnjoySockets supports **.NET 6** and **.NET 8+**, including **Native AOT** support.

> [!TIP]
> For maximum server-side performance, it is recommended to use **.NET 8 or newer** without Native AOT. This allows the JIT compiler to perform advanced, hardware-specific optimizations during runtime.

### Native AOT Configuration

To ensure your application works correctly with Native AOT, add the following sections to your `.csproj` file:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
</PropertyGroup>

<ItemGroup>
  <TrimmerRootAssembly Include="EnjoySockets" />
  <TrimmerRootAssembly Include="YourProject.Logic" />
</ItemGroup>

```

## Quick Start

Below is a minimal example showing how to set up a server and a client. EnjoySockets uses an intuitive approach where incoming messages are automatically routed to methods whose names match the target name provided as the first argument when sending a message.

### Server Side

```csharp
using EnjoySockets;

// You can generate a random key via ERSA.GeneratePrivatePublicPEM() or any other method.
string pemKeyPrivate = "-----BEGIN PRIVATE KEY-----<your_pem_key>-----END PRIVATE KEY-----";
string pemKeyPrivateSign = "-----BEGIN PRIVATE KEY-----<your_pem_key>-----END PRIVATE KEY-----";

Console.WriteLine("Starting Server...");

var server = new ETCPServer(new(pemKeyPrivate, pemKeyPrivateSign));

if (server.Start(EAddress.Get()))
    Console.WriteLine("Server started successfully!");
else
    Console.WriteLine("Failed to start server.");

Console.ReadKey();

// Logic class: Methods are automatically invoked based on the message name sent by the client.
static class ExampleReceiveClassServer
{
    static void TestMethod(EUserServer user)
    {
        Console.WriteLine("Received 'TestMethod' from client. Sending response...");
        _ = user.Send("ResponseTestMethod", Random.Shared.Next());
    }
}

```

### Client Side

```csharp
using EnjoySockets;

// Ensure these public keys match the private keys used by the server.
string pemKeyPublic = "-----BEGIN PUBLIC KEY-----<your_pem_key>-----END PUBLIC KEY-----";
string pemKeyPublicSign = "-----BEGIN PUBLIC KEY-----<your_pem_key>-----END PUBLIC KEY-----";

Console.WriteLine("Starting Client...");

var client = new EUserClient(new(pemKeyPublic, pemKeyPublicSign));

// Connect returns 0 on success
byte connectResult = await client.Connect(EAddress.Get());

if (connectResult == 0)
{
    Console.WriteLine("Connected to server.");
    // Sending a message that triggers 'TestMethod' on the server
    await client.Send("TestMethod");
}
else
{
    Console.WriteLine($"Connection failed. Error code: {connectResult}");
    Console.ReadKey();
    return;
}

Console.ReadKey();

// Logic class: Handles responses from the server.
static class ExampleReceiveClassClient
{
    static void ResponseTestMethod(EUserClient user, int luckyNumber)
    {
        Console.WriteLine($"Your lucky number from server is: {luckyNumber}");
    }
}

```
## 🔐 Encryption & Security

EnjoySockets implements a hybrid encryption stack to ensure data confidentiality, integrity, and authenticity. It combines **RSA** (identity/handshake), **ECDH** (key exchange), and **AES-256-GCM** (transport encryption).

### 🛰️ Secure Handshake Process

The library uses a robust handshake to establish a secure channel while preventing Man-in-the-Middle (MITM) attacks.

#### 1. Initial Connection
* **Client Side:** The client generates an ephemeral **ECDH** key pair (default: `nistP384`). It prepares a `ConnectDTO` containing a unique `UserId`, a `TokenToReconnect`, and the public `Key`. This payload is encrypted using the server's **RSA Public Key**.
* **Server Side:** The server decrypts the packet, uses the `TokenToReconnect` as **Salt** to derive the unique AES session key, and responds with its own ECDH public key plus an **RSA Digital Signature**.
* **Finalization:** Both parties compute the shared secret. All subsequent traffic is encrypted with **AES-256-GCM**.

#### 2. Session Reconnection & Key Rotation
EnjoySockets features a "Fast Reconnect" mechanism that maintains high security through continuous **Key Rotation**.

| Feature | Description |
| :--- | :--- |
| **Window** | Available within the `ETCPServerConfig.KeepAlive` timeframe (default: 60s). |
| **Token Rotation** | During reconnect, the client sends both the current `TokenToReconnect` (for identification) and a `NewTokenToReconnect`. |
| **Dynamic Re-keying** | Even during a simple reconnect, the server **re-mixes the AES key** using the new salt. This ensures that session keys are rotated and never static. |
| **Integrity** | If the tokens do not match the server-side state, the reconnection is rejected. |

---

### 🛡️ Cryptographic Standards

* **RSA:** Identity verification and secure delivery of the initial ECDH exchange.
* **ECDH (P-384):** Provides **Forward Secrecy**. Configurable via `ETCPConfig.ECCurve`.
* **AES-256-GCM:** Authenticated encryption (AEAD) for all messages, preventing both eavesdropping and data tampering.
* **Salted KDF:** Continuous key derivation using revolving tokens to ensure session freshness.

---

> [!TIP]
> Use `ERSA.GeneratePrivatePublicPEM()` to generate your initial key pairs for the server and client configuration.

### 🗝️ Advanced: Custom ERSA Implementation

If you choose to bypass the default PEM-based providers, you can fully customize the `ERSA` class. This is particularly useful for integrating with **Hardware Security Modules (HSM)**, **Azure Key Vault**, or other external cryptographic services.

#### Implementation Requirements

When creating a custom provider, you **must override all virtual methods** on both the Client and Server sides to ensure the handshake and transport security remain intact.

```csharp
public class MyEnterpriseERSA : ERSA
{
    public MyEnterpriseERSA() : base() { }

    // Use any key length or external provider logic here
    public override async Task<int> Encrypt(ReadOnlyMemory<byte> text, Memory<byte> destination) { /* ... */ }
    public override async Task<int> Decrypt(ReadOnlyMemory<byte> text, Memory<byte> destination) { /* ... */ }
    public override async Task<int> SignDataRsa(ReadOnlyMemory<byte> text, Memory<byte> destination) { /* ... */ }
    public override async Task<int> VerifyDataRsa(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> signature) { /* ... */ }

    public override ERSA CloneObjectToServer()
    {
        // Ensure this returns a properly initialized instance for the server factory
        return new MyEnterpriseERSA();
    }
}

```

> [!CAUTION]
> **Use at your own risk.** When implementing a custom `ERSA` provider:
> * **Completeness:** You must implement all virtual methods. Failing to do so will result in `NullReferenceException` or handshake failures.
> * **Performance:** These methods are called during critical paths. Implementations should be highly optimized and thread-safe.
> * **Compatibility:** While you can use any key length, both the Client and Server implementations must be cryptographically compatible (e.g., matching padding and hashing algorithms).
> * **Security:** You are responsible for the entropy, key storage, and overall security of your custom implementation.

## Customizing User Classes

EnjoySockets is built for extensibility. By inheriting from `EUserClient` and `EUserServer`, you can manage session states, authentication, and connection lifecycles.

### Client-Side Customization (`EUserClient`)

Inherit from `EUserClient` to handle local session logic, authorization credentials, and reconnection behavior.

> [!NOTE]
> Receiving methods (message handlers) should be placed in a separate logic class, not within this class.

```csharp
public class MyUserClient : EUserClient
{
    public MyUserClient(ERSA ersa, ETCPClientConfig config) : base(ersa, config) { }

    // Provide credentials for the handshake 
	// (can be a string, class, or any object support via 'MemoryPack')
	// Object cannot exceed 1250 bytes after serialization
    protected override object? GetAuthorization() 
    {
        return "user_login:password_hash"; 
    }

    // Possibility to provide a custom persistent GUID for this user
    protected override Guid SetGuidUserId() => base.SetGuidUserId();

    protected override void OnConnected()
    {
        // Triggered ONLY on the initial session connection. 
        // Use this to send initialization data.
        Console.WriteLine("Session established!");
    }

    protected override void OnReconnectAttempt(int attemptCount, byte attemptResult)
    {
        // Fired on every background reconnection attempt.
        // You can update the 'Address' property here to point to a fallback server,
        // log attempts, or call Disconnect() to abort the process entirely.
		// After Disconnect() and next connect attempt, OnConnected() method will be execute. 
		// 'attemptResult' will be described later in the documentation
    }

    protected override void OnDisconnected()
    {
        // Clean up local resources to prepare for a possible new session.
		// This fires when the session is permanently closed.
        Console.WriteLine("Disconnected from server.");
    }
}

```

---

### Server-Side Customization (`EUserServer`)

The `EUserServer` subclass is the heart of your user management. It allows you to validate identities, control access levels, and monitor for malicious behavior.

```csharp
public class MyUserServer : EUserServer
{
    public MyUserServer(ESocketResourceServer srs) : base(srs) { }

    /// <summary>
    /// Mandatory: Must be named 'Authorization' and return Task<byte>.
    /// The input parameter type must match the object type returned by Client's GetAuthorization().
    /// Return 0 for Success, or any value > 10 for failure (reported to the client).
    /// </summary>
    protected Task<byte> Authorization(string credentials)
    {
        // Verify user against database/identity provider
        bool isValid = credentials.Contains(":"); 
        return Task.FromResult(isValid ? (byte)0 : (byte)1);
    }

    protected override bool OnCheckAccess(long accessType)
    {
        // Thread-safe method to verify if the user has permission to call a specific method.
        // 'accessType' comes from the [EAttr.Access] attribute on the target method.
        // Note: Avoid 'async' here to prevent pausing the packet processing pipeline.
        return true; 
    }

    protected override Task OnPotentialSabotage(int msgCode)
    {
        // Handle security events:
        // 1 - Invalid packet structure (outdated or tampered client)
        // 2 - Resource Flooding (client sending too much data without reading)
        return Task.CompletedTask;
    }

    protected override void OnConnected()
    {
        // Fired only once when the session is first created.
    }

    protected override void OnDisconnected()
    {
        // Crucial: This is called when the session is officially dead (KeepAlive timeout 
        // or manual disconnect). Use this to detach static references so the GC can 
        // reclaim the memory.
    }
}

```

---

### Comparison: Lifecycle Events

| Event | `EUserClient` | `EUserServer` |
| --- | --- | --- |
| **OnConnected** | First-time handshake success. | First-time handshake success. |
| **OnReconnectAttempt** | Fires on every background attempt. | (Handled automatically by library). |
| **OnDisconnected** | When the client or server side call Disconnect(). | When KeepAlive expires or client quits. |

---

### Implementation & Usage

To utilize your custom classes instead of the default ones, initialize your client and server using the following syntax:

#### Client Side

Simply instantiate your custom class. The library handles the internal handshake and state management automatically.

```csharp
// Initialize the client with your custom logic class
var client = new MyUserClient(new ERSA(pemKeyPublic, pemKeyPublicSign), new ETCPClientConfig());

// Connect to the server using EAddress helper
byte connectResult = await client.Connect(EAddress.Get());

```

#### Server Side

When starting the server, pass your custom class as a **generic type parameter** `<T>`. This instructs the `ETCPServer` to factory-produce an instance of `MyUserServer` for every incoming connection.

```csharp
// Initialize the server with the generic user type <MyUserServer>
var server = new ETCPServer<MyUserServer>(new ERSA(pemKeyPrivate, pemKeyPrivateSign));

if (server.Start(EAddress.Get()))
{
    Console.WriteLine("Server started with MyUserServer support!");
}

```

### 🎯 Strongly Typed Handlers & Auto-Instances

EnjoySockets automatically maps incoming messages to your methods. You can organize your logic using **static classes** or **non-static logic classes**.

A key feature is **Automatic Type Injection**: the library detects your custom user type in the method signature and injects it automatically-no manual casting required.

#### Option 1: Static Logic (Simple & Global)

Best for stateless logic or globally accessible systems.

```csharp
static class PlayerLogic
{
    // The library automatically injects your custom 'MyUserClient'
    static void UpdatePosition(MyUserClient user, Vector3 position)
    {
        UI.SetLastPosition(position); 
        Console.WriteLine($"Moved to {position}");
    }
}

```

#### Option 2: Instance Logic (Automatic Lifecycle)

EnjoySockets supports stateful logic classes. You don't need to register or initialize these classes manually-the library **automatically creates an instance** the first time one of its methods is triggered. This instance then persists for subsequent calls. (More about instances later in the documentation)

```csharp
public class GameService
{
    private readonly IDatabase _db;
    private UserProfile? _cachedProfile;

    public GameService()
    {
        // Initialize your dependencies here
        _db = DB.GetDBToLogs();
    }

    // This method is routed automatically. 
    // The 'GameService' instance is created on the first call.
    public void OnProfileReceived(MyUserServer user, UserProfile profile)
    {
        _cachedProfile = profile; // This state persists in this instance
        _db.Logs.Add($"Profile {profile.Name} loaded.");
    }
}

```

> [!TIP]
> **Zero Configuration:** You don't need to call any "Register" methods. As long as the method name matches the incoming message and the signature starts with your user type (`MyUserClient`/`MyUserServer`), EnjoySockets will find it and manage the instance lifecycle for you.

## 🔌 Connection Management

EnjoySockets provides flexible ways to manage connection lifecycles, including robust automatic reconnection logic for both clients and servers.

### 🌐 Client-Side Connection

There are two primary ways to connect a client:

1. **Standard Connect:** `client.Connect(EAddress)` - A single connection attempt. Returns a status code immediately.
2. **Auto Reconnect:** `client.ConnectWithAutoReconnect(EAddress, delayMs)` - Once a session is **successfully established**, the library will automatically try to re-establish the link if it's dropped.

> [!IMPORTANT]
> **Auto Reconnect** starts working only after the first successful login. If the very first connection attempt fails (e.g., server is offline), the library will return a status code and will **not** start the automatic loop. This applies to the server-side listener as well.

#### Connection Status Codes (`byte`)

| Code | Meaning | Description |
| --- | --- | --- |
| **0** | **Success** | Connection established successfully. |
| **1** | Invalid Endpoint | The provided IP address or port is invalid. |
| **2** | Timeout | The attempt timed out. Adjust via `ETCPClientConfig.ConnectTimeout`. |
| **3** | Server Full | The server has reached its maximum connection limit. Adjust via `ETCPServerConfig.MaxSockets`. |
| **4** | Verification Failed | Handshake failed during server verification. |
| **5** | Encryption Error | Failed to establish the secure AES-256-GCM key. |
| **6** | General Failure | An unexpected network or system error occurred. |
| **7** | Auth Send Failure | Failed to deliver authorization data to the server. |
| **8** | Invalid Auth Data | Server rejected the authorization payload format. |
| **9** | Already Active | Connection is already active or an attempt is in progress. |
| **10** | Aborted | Reconnection loop was interrupted via `Disconnect()`. |

---

#### Custom Authorization logic

If you want to implement your own credential validation during the handshake, simply define the following method in your class derived from `EUserServer`:

```csharp
protected Task<byte> Authorization(T credentials)
{
    // Your logic: check database, verify tokens, etc.
    // Return 0 for Success, or any value > 10 for failure (reported to the client).
    return Task.FromResult((byte)0);
}

```

* **Optional:** If you do not define this method, the library will skip the authorization step and allow all connections that pass the cryptographic handshake.
* **Flexible Types:** The input parameter type `T` must match the object type returned by the client's `GetAuthorization()` method.
* **Return Codes:** Any value returned that is greater than `0` will be sent back to the client as a status code, terminating the connection.

#### 🛡️ Authorization & Reconnection Security

When a client uses the **Auto Reconnect** feature, the library ensures that security is never compromised.

* **Mandatory Re-Authorization:** Every time a client attempts to reconnect and resume a session, the `protected Task<byte> Authorization(T credentials)` method is executed again (if defined).
* **Identity Verification:** This mechanism is crucial to prevent unauthorized users from hijacking an existing session. Even if a malicious actor attempts to resume a session by providing a valid `TokenToReconnect`, they must still pass your custom `Authorization` logic with the correct credentials.
* **State Consistency:** By re-verifying the user during each reconnect, you ensure that the person accessing the persisted session data (like `Permissions` or `Inventory` in your custom `EUserServer`) is the same person who originally created the session.

> [!CAUTION]
> **Security Warning:** Always validate that the credentials provided during reconnection match the original session owner. If the `Authorization` method returns a non-zero value during a reconnect attempt, the library will immediately terminate the connection and mark the session as inaccessible for that attempt.

### 🛑 Disconnect & Resource Cleanup

#### Client Reusability

The `EUserClient` (or your custom class) is **reusable**. When you call `client.Disconnect()`, the library cleans up the internal state and prepares the object for a fresh connection. You do **not** need to create a new object to log in as a different user.

#### Server-Side Session & Auto-Instances

Unlike the client, `EUserServer` instances are **not reusable**.

* **Status: Dead** - Once a session status changes to `Dead`, the object is finished. The user associated with it will never return to this specific instance.
* **Automatic Instance Cleanup** - When a session becomes `Dead`, the library automatically "unhooks" the logic instances (e.g., `GameService`) associated with that user.

### ⚠️ Critical: Memory Management & Resource Disposal

Managing unmanaged resources (e.g., timers, database connections, or manual event subscriptions) requires special attention because **automatic logic instances do not have their own disconnection hooks.**

#### The Problem

When a session becomes `Dead`, the library detaches the logic instances (like `GameService`). However, if a logic instance holds an active resource (like a running `System.Timers.Timer`), that resource remains rooted in memory by the system. Since the instance is now detached and "blind" to the session's end, it will never stop the resource, leading to a **permanent memory leak**.

#### The Solution: The "Resource Anchor" Pattern

The `EUserServer` object is the **only** component with a guaranteed `OnDisconnected` lifecycle trigger. You should bridge your resources from the logic instance to the user object or other managed place.

**Recommended Implementation:**

```csharp
// 1. The Anchor Class (EUserServer)
public class MyUserServer : EUserServer
{
    // A collection to hold resources needing manual disposal
    public List<IDisposable> ResourcesToDispose { get; } = new();

    public MyUserServer(ESocketResourceServer srs) : base(srs) { }

    protected override void OnDisconnected()
    {
        // 3. The only guaranteed place to clean up everything
        foreach (var resource in ResourcesToDispose)
        {
            try { resource.Dispose(); } catch { /* Log error */ }
        }
        ResourcesToDispose.Clear();
    }
}

// 2. The Logic Class
public class GameService
{
    // Instance-level state
    private System.Timers.Timer? MyTimer { get; set; }

    public void StartSensitiveTask(MyUserServer user)
    {
        if (MyTimer == null)
        {
            MyTimer = new System.Timers.Timer(1000);
            
            // IMPORTANT: Anchor the resource to the user object immediately.
            // This ensures that when the user disconnects, this timer is stopped/disposed.
            user.ResourcesToDispose.Add(MyTimer);
        }
        
        MyTimer.Start();
    }
}

```

This pattern is also applicable to the client side. Since session instances are cleared automatically, you must ensure all resources are properly disposed of to prevent memory leaks.

> [!CAUTION]
> **Why is this necessary?** Even if you don't keep a reference to a `Timer` or `Socket` in a static list, the .NET Runtime itself might keep them alive while they are active. Always use the `EUserServer.OnDisconnected()` method to explicitly shut down these resources.

## 📤 Sending Messages

EnjoySockets offers different sending strategies optimized for the specific roles of the Client and the Server.

### Core Sending Methods

Both sides share a consistent API for basic communication:

* `user.Send("Target", data);` - Sends a message with a payload.
* `user.Send("Target");` - Sends a signal without a payload.
* `user.Send(instanceId, "Target", data);` - Routes a message to a specific object instance.

---

### 🖥️ Server-Side: Built for High Throughput

On the server, performance is the absolute priority. The library focuses on pushing data to the OS socket buffer as fast as possible.

* **Non-blocking:** The `Send` method returns a `bool` immediately, indicating if the data was successfully buffered.
* **Pre-serialized Buffers:** For maximum efficiency (e.g., in **Multicast** scenarios), the server can send raw `ReadOnlyMemory<byte>`. This avoids redundant serialization when sending the same data to thousands of clients.

```csharp
// Multicast example
var serializedData = ESerial.Serialize(myObject);
foreach (var user in connectedUsers)
{
    user.Send("Target", serializedData); // Extremely fast, no re-serialization
}

```

---

### 🌐 Client-Side: Reliability & Flow Control

The client features a more sophisticated transmission engine designed to handle real-world network instability.

#### ⚖️ Smart Flow Control

The library monitors the server's capacity and internal buffers. It automatically throttles outgoing messages if there is a risk of overwhelming the server. This mechanism ensures that you should never encounter a "Buffer Full" error under normal conditions.

> [!TIP]
> Ensure that **ETCPConfig.MessageBuffer** is set to the same value on both the client and server sides.

#### 🔄 Reliable Requests: `SendWithResponse`

Unlike a standard fire-and-forget `Send`, `SendWithResponse` is an advanced RPC-like tool that returns a `long` status or ID.

* **Guaranteed Execution:** If the connection drops after calling `SendWithResponse`, the library tracks the message state. Once reconnected, it ensures the message is delivered and the response is retrieved.
* **State Persistence:** Even if the client is temporarily offline, the server continues processing the logic to ensure a consistent result is ready when the client returns.
* **Result Codes:**
* `0` and above: Success (often used for IDs or custom status codes).
* `-2`: Buffer full (system-level error).
* `-3`: **Session Expired.** The response could not be retrieved because the session was permanently closed (e.g., via `Disconnect()` or a `KeepAlive` timeout).

> [!IMPORTANT]
> **Keep-Alive Tuning:** To ensure `SendWithResponse` can recover from longer outages, make sure `ETCPServerConfig.KeepAlive` is set to a value that allows your clients enough time to reconnect and claim their pending responses.

---

### 💡 Why use `SendWithResponse`?

It is the perfect choice for critical operations where you cannot afford to lose track of the result, such as:

1. **Object Registration:** Creating a new entity on the server and needing its `instanceId`.
2. **Transactions:** Adding an item to an inventory and waiting for confirmation.
3. **Complex Handshakes:** Any logic that requires a reliable "Acknowledge" from the server.

## 📥 Receiving Messages

To handle incoming data, you create **Access Points** (methods) with a specific signature. The library automatically maps incoming messages to these methods based on their names.

### Method Signature Requirements

1. **First Parameter (Mandatory):** Must be `EUser` or any derived type (e.g., `MyUserServer` or `MyUserClient`).
2. **Second Parameter (Optional):** A single object of any type supported by **MemoryPack**. Only one payload parameter is allowed.
3. **Return Types:**
* **Server-side:** `void`, `Task`, `long`, or `Task<long>`.
* **Client-side:** `void` or `Task` (clients do not return values to the server).


4. **No `async void`:** Methods marked as `async void` are not supported and will not be mapped.

---

### 🛠️ The `EAttr` Attribute (Execution Control)

You can control how each method (or an entire class) behaves using the `[EAttr]` attribute. If applied to a class, all methods within it inherit the configuration unless overridden by a specific attribute on the method itself.

#### Key Properties:

* **`Access` (long):** A custom identifier used to verify if a user can call this method. Logic is handled via `OnCheckAccess` in your `EUserServer` class.
* **`MaxParamSize` (int):** The maximum allowed size (in bytes) of the serialized parameter. Essential for protecting against large-payload attacks.
* **`PoolId` (ushort):** Specifies which Object Pool configuration to use for the parameter (optimized memory usage).
* **`ChannelId` (ushort):** Defines the **Execution Channel**. By default, messages are executed sequentially on a single thread (Channel 0). Custom channels allow for parallel or private execution flows.

---

### 📋 Configuration Strategy

For better maintainability, it is recommended to define your IDs in centralized static classes using `[EAttrChannel]` and `[EAttrPool]`.

```csharp
public static class ChannelIDs
{
    [EAttrChannel(ChannelType = EChannelType.Private, ChannelTasks = 1)]
    public const ushort Basic = 1;

    [EAttrChannel(ChannelType = EChannelType.Share, ChannelTasks = 1)]
    public const ushort SpecialShare = 2;
}

public static class PoolIDs
{
    [EAttrPool(MaxPoolObjs = 5000)]
    public const ushort Basic = 1;

    [EAttrPool(MaxPoolObjs = 100)]
    public const ushort BasicMin = 2;
}

```

### Example: Using Attributes and Inheritance

You can define custom attribute presets to avoid repetitive code:

```csharp
// 1. Custom Attribute Preset (Inherits from EAttr)
public class EAttrCustom : EAttr
{
    public EAttrCustom() 
    {
        MaxParamSize = 1024;
        Access = 5;
        PoolId = PoolIDs.BasicMin;
        ChannelId = ChannelIDs.Basic;
    }
}

// 2. Implementation with Inheritance and Overrides
[EAttr(MaxParamSize = 8100, Access = 10, PoolId = PoolIDs.Basic)]
public class TestReceiveClass
{
    // MaxParamSize = 8100, Access = 2, PoolId = PoolIDs.Basic, ChannelId = ChannelIDs.SpecialShare
    [EAttr(Access = 2, ChannelId = ChannelIDs.SpecialShare)]
    public void MethodA(MyUserClient user, List<long> data)
    {
        // Inherits MaxParamSize and PoolId from class. Overrides Access and ChannelId.
    }

    // MaxParamSize = 16200, Access = 10, PoolId = PoolIDs.Basic, ChannelId = 0 (default)
    [EAttr(MaxParamSize = 16200)]
    public static void MethodB(MyUserClient user, List<long> data)
    {
        // Inherits Access and PoolId from class. Overrides MaxParamSize.
    }

    // MaxParamSize = 1024, Access = 5, PoolId = PoolIDs.BasicMin, ChannelId = ChannelIDs.Basic
    [EAttrCustom]
    public void MethodC(MyUserClient user, string message)
    {
        // Note: Since EAttrCustom sets ALL properties in its constructor, 
        // it effectively overrides everything from the class level.
    }
}

```

---

### 💡 Why use Channels and Pools?

* **Channels:** Prevent "bottlenecks". A slow database save in one channel won't stop fast movement updates in another.
* **Pools:** Drastically reduce **GC Pressure**. Instead of creating new objects for every message, the library reuses them from the pool.
* **Access IDs:** Simplify permission management. You can check a user's database-stored rank against the `Access` ID in one central method.

---

## 🧩 Instance Architecture

EnjoySockets routing is **name-based**. To ensure your logic executes correctly, you must follow one golden rule:

> [!WARNING]
> **Unique Method Names:** Every access point (method) must have a unique name across your entire project. If multiple methods share the same name, the library will only map the first one it encounters during the scanning process.

---

### 🏛️ Three Ways to Handle Logic

You can organize your code using three distinct patterns, depending on whether you need shared state or private session data.

1. **Static Logic (Global):** Shared across all sockets. Best for global systems (e.g., a world chat or global stats).
2. **Auto-Private Instances (Stateful):** Automatically created for each socket. Requires a **parameterless constructor** and at least one valid non-static access method.
3. **Hybrid Approach:** A single class containing both static and instance methods.

#### Example: Static vs. Auto-Private

```csharp
public class PlayerService
{
    // GLOBAL: Shared by all players
    private static int _totalPlayersOnline;

    // PRIVATE: Unique to every connected socket
    private int _localActionCount = 0;

    public PlayerService() { /* Library calls this automatically */ }

    // This is a global access point
    public static void GetGlobalStats(MyUserServer user)
    {
        user.Send("OnStatsReceived", _totalPlayersOnline);
    }

    // This is a private access point (Stateful)
    public void PerformAction(MyUserServer user)
    {
        _localActionCount++;
        Console.WriteLine($"User {user.EndPointSocket} performed {_localActionCount} actions.");
    }
}

```

### 🚀 Registered Instances (`InstanceRegister`)

Sometimes, "one instance per type" isn't enough. For example, if a user is participating in **two different matches** simultaneously, both handled by the same `GameService` class.

By using `user.InstanceRegister(object)`, you gain total control:

* **Custom Constructors:** You can pass any parameters to your instance before registering it.
* **Multiple Instances:** You can register multiple objects of the same type for a single user.
* **Instance IDs:** The method returns a `long` ID. You use this ID to route messages to that specific object.

#### Registered Instances in Practice

The most common use case for `InstanceRegister` is when a client needs to interact with a specific object on the server (e.g., a specific Match, Trade, or Dialogue session).

##### 1. Server-Side: Registration & ID Delivery

The server creates the instance and returns the unique ID to the client. Using `SendWithResponse` on the client side is the cleanest way to handle this.

```csharp
// Server-side logic (e.g., in a LobbyService)
public class PlayerMatchService
{
	List<long> _currentMatches = new();
	
	public long CreateMatch(MyUserServer user, string mapName)
	{
		// 1. Create the specific instance
		var newMatch = new GameMatch(mapName);
		
		// 2. Register it and get the unique ID for THIS user
		long instanceId = user.InstanceRegister(newMatch);
		
		_currentMatches.Add(instanceId);
		
		// 3. Return the ID so the client knows how to address this match
		return instanceId; 
	}
}

```

##### 2. Client-Side: Interaction using the ID

Once the client has the ID, they can target that specific instance on the server.

```csharp
// Client-side logic
public async Task StartGame()
{
    // A. Request match creation and get the ID back
    long matchId = await client.SendWithResponse("CreateMatch", "CyberCity");

    if (matchId > 0)
    {
        // B. Send messages directly to that specific GameMatch instance on the server
        client.Send(matchId, "JoinTeam", "Blue");
        client.Send(matchId, "ReadyUp");
    }
}

```

#### 💡 Why this is powerful?

* **No Ambiguity:** The client can be part of multiple "Matches" or "Trades" at once. By passing the `instanceId`, the server knows exactly which object should handle the incoming `JoinTeam` or `ReadyUp` call.
* **Encapsulation:** The `GameMatch` class doesn't need to know about other matches. It only cares about the logic for its specific instance.
* **Memory Efficiency:** You only register what you need. When the match ends, a simple `user.InstanceRemove(matchId)` on the server cleans up the routing table for that user.

#### Shared Registered Instances

If you want multiple users to interact with the **same physical object**, simply register that object for each user.

* Each user will get a **unique ID** for that shared object within their own session.
* This is perfect for "Rooms" or "Party" logic where a single `Match` object is shared among 4 players.

#### Cleanup

* `user.InstanceRemove(id)`: Removes a specific registered instance.
* `user.InstanceDetach()`: Clears **all** registered instances for that user.

> [!NOTE]
> **Important:** Even if multiple users are in the same `GameMatch` object, each user will have their own unique `instanceId` mapping to it. This keeps the internal routing table of each `EUserServer` fast and isolated.

### 🧬 Inheritance in Instances

EnjoySockets supports class inheritance, making it easy to extend logic without breaking your routing.

1. **The "Most Derived" Rule:** If you have a class hierarchy, the library will map methods found **furthest from the base class**.
2. **Auto-Instances:** The library will always instantiate the most derived class that meets the "Auto-Instance" criteria (parameterless constructor + valid methods).

#### Example: Inheritance Mapping

```csharp
public class BaseLogic 
{
    public virtual void OnPing(MyUserServer user) => Console.WriteLine("Base Ping");
}

public class AdvancedLogic : BaseLogic
{
    // The library will map THIS method because it's further from the base
    public override void OnPing(MyUserServer user) => Console.WriteLine("Advanced Ping!");
    
    public void OnSpecialAction(MyUserServer user) { /* ... */ }
}

```

> [!TIP]
> When using `InstanceRegister()`, you can pass any object from your inheritance tree. As long as it has at least one valid, non-static access method, the library will handle the routing perfectly.

## Object Pooling

To reduce **GC (Garbage Collector) pressure** and achieve high-performance data processing, EnjoySockets features a built-in object pooling system. This allows the library to recycle objects instead of allocating new memory every time a message is received.

### Registering a Pool

To register a pool, define a `const ushort` variable anywhere in your code and mark it with the `[EAttrPool]` attribute. It is highly recommended to group these IDs within a single static class for better maintainability.

```csharp
public static class PoolIDs
{
    // MaxPoolObjs defines the limit of objects held in the pool.
    // Setting it >0
    [EAttrPool(MaxPoolObjs = 5000)]
    public const ushort Basic = 1;

    [EAttrPool(MaxPoolObjs = 100)]
    public const ushort LargeBuffers = 2;
}

```

### **Usage Example**

Applying a pool to a method is straightforward. Simply reference your predefined `PoolId` within the `[EAttr]` attribute:

```csharp
// Example: Applying the 'LargeBuffers' pool to a specific access point
[EAttr(PoolId = PoolIDs.LargeBuffers)]
public static void ProcessLargeData(MyUserServer user, List<long> data)
{
    // 'data' is automatically retrieved from the pool.
    // Ensure you do not store a reference to 'data' outside this method!
}

```

> [!WARNING]
> **Unique Identifiers:** Pool IDs must be unique. If two constants share the same ID, only one will be mapped by the library.

---

### ⚠️ Critical Rules for Pooling

Object pooling is powerful but requires strict adherence to memory safety:

1. **Lifecycle:** An object taken from the pool is **automatically returned** to the pool as soon as the receiving method finishes execution.
2. **No Persistent References:** **NEVER** store a reference to a pooled object (e.g., in a static list or a class field) after the method ends. Since the object is recycled, its data will be overwritten by the next incoming message, leading to unpredictable bugs and data corruption.
3. **Persistence Strategy:** If you need to keep the data from a pooled object, you must **copy the data** to a new instance or simply disable pooling for that specific access point.

---

### 🔍 Optimization Guide: What to Pool?

Not all types benefit equally from pooling. Follow these guidelines to maximize efficiency:

* **Classes & Lists (Best Choice):** This is where pooling shines. `List<T>` is particularly effective because the library reuses the internal buffer without re-allocating it.
* **Immutable Types (Do Not Pool):** Types like `string`, `DateTime`, or `Guid` should not be pooled. Because they are immutable, they are always re-allocated during deserialization anyway.
* **Hybrid Classes:** If you have a class containing a `string` and a `List<int>`, pooling the class is still beneficial. While the string will be a new allocation, the class shell and the list structure will be reused.
* **Primitives (`int`, `long`, etc.):** Generally not worth pooling directly.
* *Pro Tip:* For **Zero-Allocation** targets, wrap primitives in a class. Deserializing a raw primitive always costs a few bytes of allocation, but using a pooled class wrapper allows for a completely zero-allocation flow.

### Lists vs. Arrays

While the library supports pooling for arrays (`T[]`), they are only recycled if the incoming data length **exactly matches** the array size in the pool.

* **Recommendation:** Always prefer `List<T>` over arrays for receiving data. Lists are much more flexible and are reused efficiently regardless of the number of elements they contain.

> [!TIP]
> For deeper technical insights into how memory is managed during deserialization, refer to the **MemoryPack** documentation, which serves as the core engine for EnjoySockets.

## 🚦 Flow Control: Channels

Channels are a powerful tool used to control how many methods are processed concurrently. They allow you to maximize server performance by ensuring that "heavy" operations (like database I/O or intensive CPU tasks) do not block the rest of your system.

By using channels, you can isolate different parts of your logic into separate execution pipelines.

### Channel Types

1. **Private:** This channel is scoped to a **single socket (user)**. If a user sends multiple requests to a method on a private channel, those requests are queued and processed specifically for that user.
2. **Share (Global):** This channel is **shared across all sockets** on the entire server. It creates a single, global queue for all users. This is primarily used on the server side to manage shared resources.

> [!NOTE]
> **Default Behavior:** If no channel is specified for a method, the library automatically uses a **Private channel with 1 task**.

### Channel Registration

Channels are registered using a `const ushort` with the `[EAttrChannel]` attribute. You can define the type and the number of concurrent tasks allowed.

```csharp
public static class ChannelIDs
{
    // Each user has their own sequential queue for basic actions
    [EAttrChannel(ChannelType = EChannelType.Private, ChannelTasks = 1)]
    public const ushort Basic = 1;

    // A global shared queue for operations involving shared data (e.g., Products)
    [EAttrChannel(ChannelType = EChannelType.Share, ChannelTasks = 1)]
    public const ushort InventorySync = 2;
}

```

### 🛡️ The "Lock-Free" Pattern (Shared Sequential Execution)

One of the most efficient ways to use channels is setting `ChannelType = EChannelType.Share` with `ChannelTasks = 1`.

This configuration acts as a **global serialized queue**. When multiple methods (even in different classes) are assigned to this channel, the library ensures that only **one method is executed at a time across the entire server**. This eliminates the need for manual `lock` statements, which reduces thread contention and improves performance under high load.

#### Example: Thread-Safe Inventory Management

In this example, we manage a global product list. By using a shared channel with one task, we don't need to use `lock` inside our methods.

```csharp
public static class ProductService
{
    // This list is shared across all users. 
    // Usually, we would need a lock() to modify it safely.
    private static readonly List<string> GlobalProducts = new();

    // All three methods share ChannelIDs.InventorySync (Share, Task = 1)
    
    [EAttr(ChannelId = ChannelIDs.InventorySync)]
    public static void AddProduct(MyUserServer user, string name)
    {
        // No lock needed! The channel ensures sequential access.
        GlobalProducts.Add(name);
        user.Send("OnProductAdded", true);
    }

    [EAttr(ChannelId = ChannelIDs.InventorySync)]
    public static void RemoveProduct(MyUserServer user, string name)
    {
        // No lock needed!
        GlobalProducts.Remove(name);
        user.Send("OnProductRemoved", true);
    }

    [EAttr(ChannelId = ChannelIDs.InventorySync)]
    public static void GetProductCount(MyUserServer user)
    {
        // Even reading is safe because no 'Add' or 'Remove' can happen simultaneously.
        user.Send("OnCountReceived", GlobalProducts.Count);
    }
}

```

### 💡 Advanced: Shared Channels in Instance Methods

It is important to note that **`EChannelType.Share` is not limited to `static` methods.** You can apply it to instance methods as well, including those within **automatic private instances**.

Even if every user has their own separate instance of a class, if a method within that class is marked with a **Shared Channel**, the library will force a single, global queue for that method across **all users and all instances**.

#### Example: Instance-based Global Queue

Imagine every user has a `TradeService` instance, but the actual execution of a trade must be serialized globally to prevent race conditions in your database.

```csharp
public class TradeService
{
    // Even though this is an instance method and every user has their own TradeService...
    [EAttr(ChannelId = ChannelIDs.GlobalTradeLock)] 
    public void ExecuteTrade(MyUserServer user, TradeRequest request)
    {
        // ...this code will only run for ONE user at a time globally 
        // because ChannelIDs.GlobalTradeLock is EChannelType.Share with 1 task.
    }
}

```

This hybrid approach gives you the best of both worlds: you can keep your logic organized in instances while still enforcing global synchronization where it matters most.

### 💡 Why use Channels?

* **Isolation:** A slow database query in an "Accounting" channel won't slow down the movement updates in a "Physics" channel.
* **Performance:** Avoiding `lock` blocks reduces CPU context switching and prevents "Thread Starvation."
* **Predictability:** You can strictly control how many resources are dedicated to specific types of incoming traffic.

## 📢 Multicasting

EnjoySockets **does not** include a built-in, "one-size-fits-all" multicast function. In complex systems, message routing logic (groups, permissions, instance visibility) varies so much that a generic implementation often becomes a bottleneck or a source of architectural "bloat."

Instead, the library provides high-performance tools so you can build a multicast system tailored to your specific needs.

### The Golden Rule: Serialize Once, Send Many

The most common mistake in networking is serializing the same object multiple times for different recipients. To achieve maximum throughput, you should:

1. Serialize your data **once** into a buffer.
2. Iterate through your target users.
3. Send the **raw bytes** using the specific `Send` overload.

### High-Performance Serialization

You have two ways to prepare your data for multicast:

#### 1. Standard Serialization (Allocating)

Best for quick operations or when the frequency of multicasts is low.

```csharp
var serializedData = ESerial.Serialize(myData); // Allocates a new byte array
foreach (var user in roomUsers)
{
    user.Send("OnUpdate", serializedData);
}

```

#### 2. Zero-Allocation Serialization (Recommended)

For high-frequency updates (e.g., player positions), use `EArrayBufferWriter`. This avoids creating new objects for the Garbage Collector to clean up.

```csharp
// Define this outside your loop/method to reuse the memory
private readonly EArrayBufferWriter _buffer = new(10 * 1024); // for example - 10KB initial capacity

public void BroadcastToAll(IEnumerable<MyUserServer> users, MyData data)
{
    _buffer.ResetWrittenCount(); // Reset the writer without deallocating the internal array
    ESerial.Serialize(_buffer, data);
    
    ReadOnlyMemory<byte> bytes = _buffer.WrittenMemory;

    foreach (var user in users)
    {
        // High-speed transfer of raw memory
        user.Send("OnUpdate", bytes);
    }
}

```

> [!TIP]
> By using `EArrayBufferWriter` and the `ReadOnlyMemory<byte>` overload of `Send`, you bypass the serialization logic for every user in the loop. This can result **performance increase** for large groups.

## 🌐 Scalability & Distributed Systems

Currently, EnjoySockets is optimized for high-performance **Vertical Scaling**. Thanks to its memory-efficient architecture and low-level optimizations, a single instance can comfortably handle thousands of concurrent users depending on the complexity of your business logic.

### Scaling Beyond a Single Server

As your project grows, you may eventually reach the limits of vertical expansion (adding more CPU/RAM). In such cases, **Horizontal Scaling** (adding more server nodes) becomes necessary.

* **Current Architecture:** At this stage, the library does not provide a built-in "out-of-the-box" distributed system (like automatic state synchronization across nodes).
* **Standard Approach:** For distributed environments, it is recommended to treat each server instance as an independent node. Shared state should be managed via a common persistence layer, such as a centralized SQL database or a distributed NoSQL solution (e.g., Redis for session state, MongoDB for persistence).
* **Infrastructure Requirements:** Moving to a distributed system requires appropriate IT infrastructure to manage server health, load balancing, and database synchronization.

### Proxy & Custom Solutions

The library's high performance and flexible routing make it an excellent choice for building **custom proxy or gateway nodes**. You can use EnjoySockets to create a lightweight entry point that routes traffic to various backend microservices based on your specific requirements.

### Future Roadmap

Distributed systems are complex and highly dependent on individual use cases. If there is significant community demand for built-in distributed features (such as a cluster provider or message bus integration), I am open to developing these modules as the library evolves.
