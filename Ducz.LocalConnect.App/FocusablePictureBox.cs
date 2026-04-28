namespace Ducz.LocalConnect.App;

internal sealed class FocusablePictureBox : PictureBox
{
    public FocusablePictureBox()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return true;
    }
}