namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigSingboxService
{
    private void GenLog()
    {
        try
        {
            switch (_config.CoreBasicItem.Loglevel)
            {
                case "debug":
                case "info":
                case "error":
                    _coreConfig.log.level = _config.CoreBasicItem.Loglevel;
                    break;

                case "warning":
                    _coreConfig.log.level = "warn";
                    break;

                default:
                    break;
            }
            if (_config.CoreBasicItem.Loglevel == Global.None)
            {
                _coreConfig.log.disabled = true;
            }
            // Never point the core at a plaintext log file — its stdout is captured
            // by the app and persisted through the encrypted log store.
            _coreConfig.log.output = null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }
}
