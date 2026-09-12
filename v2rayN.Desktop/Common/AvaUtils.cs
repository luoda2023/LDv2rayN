using Avalonia.Input.Platform;

namespace v2rayN.Desktop.Common;

internal class AvaUtils
{
    public static async Task<string?> GetClipboardData(Window owner)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
            if (clipboard == null)
            {
                return null;
            }

            return await clipboard.TryGetTextAsync();
        }
        catch
        {
            return null;
        }
    }

    public static async Task SetClipboardData(Visual? visual, string strData)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(visual)?.Clipboard;
            if (clipboard == null)
            {
                return;
            }

            await clipboard.SetTextAsync(strData);
        }
        catch
        {
        }
    }

    /// <summary>
    /// 品牌图标：未连接用 LDv2rayN.png（仓库根），连接成功后托盘切红色 LDv2rayN2.png。
    /// 两张图都是仓库根目录的原文件，不许换成别的。
    /// </summary>
    public static WindowIcon GetAppIcon(bool connected = false)
    {
        var uri = connected
            ? new Uri("avares://LDv2rayN/LDv2rayN2.png")
            : new Uri("avares://LDv2rayN/LDv2rayN.png");
        using var bitmap = new Bitmap(AssetLoader.Open(uri));
        return new(bitmap);
    }
}
