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
            return Task.FromResult(LoadBrandIcon(BrandIconPath));
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return Task.FromResult(Properties.Resources.NotifyIcon1);
        }
    }

    /// <summary>品牌图标（未连接状态）。源文件是仓库根目录的 LDv2rayN.png，由此生成 ico。</summary>
    public const string BrandIconPath = "/Resources/LDv2rayN.ico";

    /// <summary>已连接状态的品牌图标。源文件是仓库根目录的 LDv2rayN2.png（红色）。</summary>
    public const string BrandIconConnectedPath = "/Resources/LDv2rayN2.ico";

    private static Icon LoadBrandIcon(string path)
    {
        // 必须用 .ico：System.Drawing.Icon(Stream) 只认 ico 格式，
        // 喂 PNG 流会抛异常（这里以前就是这么错的，结果一直回退到旧图标）。
        var resource = Application.GetResourceStream(new Uri(path, UriKind.Relative));
        if (resource?.Stream is null)
        {
            throw new InvalidOperationException($"Icon resource was not found: {path}");
        }

        using var stream = resource.Stream;
        return new Icon(stream);
    }

    public System.Windows.Media.ImageSource GetAppIcon(Config config)
    {
        // 用 ico 而不是原图 PNG：ico 里内嵌 16/32/48/256 多尺寸，
        // 任务栏和 Alt+Tab 的小尺寸不会糊。
        return new BitmapImage(new Uri("pack://application:,,,/Resources/LDv2rayN.ico", UriKind.Absolute));
    }

    public void RegisterGlobalHotkey(Config config, Action<EGlobalHotkey> handler, Action<bool, string>? update)
    {
        HotkeyManager.Instance.UpdateViewEvent += update;
        HotkeyManager.Instance.HotkeyTriggerEvent += handler;
        HotkeyManager.Instance.Load();
    }
}
