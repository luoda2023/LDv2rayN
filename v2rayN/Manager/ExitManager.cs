namespace v2rayN.Manager;

/// <summary>
/// Shared flag that indicates a real exit is in progress. Without this,
/// MainWindow_Closing would cancel the close (to hide-to-tray) and
/// Application.Current.Shutdown() would never actually shut down WPF.
/// </summary>
public static class ExitManager
{
    public static volatile bool ForceExit;
}
