namespace RevitMCP.Contracts;

public static class BridgeErrorCodes
{
    public const string ProtocolIncompatible = "BRIDGE_PROTOCOL_INCOMPATIBLE";
    public const string IdentityMismatch = "BRIDGE_IDENTITY_MISMATCH";
    public const string HandshakeTimeout = "BRIDGE_HANDSHAKE_TIMEOUT";
    public const string HandshakeFailed = "BRIDGE_HANDSHAKE_FAILED";
}
