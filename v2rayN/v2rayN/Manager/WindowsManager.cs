using System.Drawing;
using System.Windows.Media.Imaging;

namespace v2rayN.Manager;

public sealed class WindowsManager
{
    private static readonly Lazy<WindowsManager> instance = new(() => new());
    public static WindowsManager Instance => instance.Value;
    private static readonly string _tag = "WindowsHandler";

    public Task<Icon> GetNotifyIcon(Config config)
    {
        try
        {
            return Task.FromResult(LoadBrandIcon());
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return Task.FromResult(Properties.Resources.NotifyIcon1);
        }
    }

    private static Icon LoadBrandIcon()
    {
        var resource = Application.GetResourceStream(new Uri("/Resources/LDv2rayN.png", UriKind.Relative));
        if (resource?.Stream is null)
        {
            throw new InvalidOperationException("LDv2rayN icon resource was not found.");
        }

        using var stream = resource.Stream;
        return new Icon(stream);
    }

    public System.Windows.Media.ImageSource GetAppIcon(Config config)
    {
        return new BitmapImage(new Uri("pack://application:,,,/Resources/LDv2rayN.png", UriKind.Absolute));
    }

    public void RegisterGlobalHotkey(Config config, Action<EGlobalHotkey> handler, Action<bool, string>? update)
    {
        HotkeyManager.Instance.UpdateViewEvent += update;
        HotkeyManager.Instance.HotkeyTriggerEvent += handler;
        HotkeyManager.Instance.Load();
    }
}
