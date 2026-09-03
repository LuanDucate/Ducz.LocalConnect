namespace Ducz.LocalConnect.Core.Protocol;

public sealed class ProtocolException : Exception
{
    public ProtocolException(string message)
        : base(message)
    {
    }
}
