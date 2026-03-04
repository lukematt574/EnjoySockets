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

Below is a minimal example showing how to set up a server and a client. EnjoySockets uses an intuitive approach where incoming messages are automatically routed to methods that match the message name.

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

## 🛠️ Customizing User Classes

EnjoySockets is built for extensibility. By inheriting from `EUserClient` and `EUserServer`, you can manage session states, authentication, and connection lifecycles.

### Client-Side Customization (`EUserClient`)

Inherit from `EUserClient` to handle local session logic, authorization credentials, and reconnection behavior.

> [!NOTE]
> Receiving methods (message handlers) should be placed in a separate logic class, not within this class.

```csharp
public class MyUserClient : EUserClient
{
    public MyUserClient(ERSA ersa, ETCPClientConfig config) : base(ersa, config) { }

    // Provide credentials for the handshake (can be a string, class, or any object support via 'MemoryPack')
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
        // Clean up local resources to prepare for a possible new session. This fires when the session is permanently closed.
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
    /// Return 0 for success.
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
| **OnDisconnected** | When the client call Disconnect(). | When KeepAlive expires or client quits. |

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
		var text = $"Profile {profile.Name} loaded.";
        _db.Logs.Add(text);
        Console.WriteLine(text);
    }
}

```

> [!TIP]
> **Zero Configuration:** You don't need to call any "Register" methods. As long as the method name matches the incoming message and the signature starts with your user type (`MyUserClient`/`MyUserServer`), EnjoySockets will find it and manage the instance lifecycle for you.
