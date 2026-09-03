using System.Drawing;

namespace Ducz.LocalConnect.Core.Capture;

public interface IScreenSource : IDisposable
{
    Rectangle Bounds { get; }
    void Capture(Bitmap target);
}
