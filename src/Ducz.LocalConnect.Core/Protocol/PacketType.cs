namespace Ducz.LocalConnect.Core.Protocol;

public enum PacketType : byte
{
    Handshake = 1,
    HandshakeResult = 2,
    Frame = 3,
    MouseMove = 4,
    MouseDown = 5,
    MouseUp = 6,
    MouseWheel = 7,
    KeyDown = 8,
    KeyUp = 9,
    ClipboardSet = 10,
    ClipboardRequest = 11,
    ClipboardResponse = 12,
    FileMetadata = 13,
    FileChunk = 14,
    MonitorSelect = 15,
    MonitorList = 16,
    Ping = 17,
    Pong = 18,
    QualitySelect = 19,
    SecureAttention = 20,
    StreamControl = 21,
}
