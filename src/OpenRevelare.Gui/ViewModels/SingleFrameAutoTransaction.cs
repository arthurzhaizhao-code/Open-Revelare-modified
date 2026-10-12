namespace OpenRevelare.Gui.ViewModels;

/// <summary>
/// Runs the current-frame highlight measurement as one transaction.  D_min is deliberately not
/// measured here: it is a physical property of the roll and the caller keeps the current roll
/// value fixed.  The caller owns the state snapshot; this helper guarantees that every
/// unsuccessful exit, including an exception, restores it exactly once.
/// </summary>
internal static class SingleFrameAutoTransaction
{
    public static bool Run(Func<bool> measureHighlight, Action rollback)
    {
        bool committed = false;
        try
        {
            if (!measureHighlight()) return false;
            committed = true;
            return true;
        }
        finally
        {
            if (!committed) rollback();
        }
    }
}
