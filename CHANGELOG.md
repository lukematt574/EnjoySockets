# Change Log

## v1.4.1

- Preserved AES session keys across reconnects within the same session
- Added missing result code coverage
- Refactored and updated XML documentation

## v1.4.0

- Added `SendAndFetch` request/response API
- Refactored framework naming and package structure (no behavioral changes)
- Improved result code architecture and handling

## v1.3.1

- Exposed active serializer in endpoint user context for runtime usage inside handlers

## v1.3.0

- Replace DTO-based protocol handling with Span/Memory parsing
- Added customizable serializer support via `IESerializer` interface
- Fix `MaxPoolObjs` from `EAttrPool` during pool initialization

## v1.2.1

- Refactored write-to-receive channel handling with improved socket shutdown behavior after full server errors
- Separated rented resources per client
- Introduced segment-based send path and `SendSerialized`
- Fixed handshake signature verification
- Added support for custom attributes based on inherited `EAttr`
- Updated packet send/receive format with configurable size via `ETCPConfig.MaxPacketSize`
- Improved method invocation performance using delegates instead of reflection (JIT mode only)
- Added `ETCPServer.IsJIT` control property
- Minor bug fixes

## v1.1.0

- Ability to invoke private methods (based on RPC method signature rules)
- Support for ECCurve nistP521
- Documentation updates
- Added Apache 2.0 license comments in source code

## v1.0.0

- First Package