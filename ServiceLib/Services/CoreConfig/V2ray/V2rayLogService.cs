namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigV2rayService
{
    private void GenLog()
    {
        try
        {
            _coreConfig.log.loglevel = _config.CoreBasicItem.Loglevel;
            // Never let the core write plaintext log files. Core stdout/stderr is
            // captured by the app and persisted through the encrypted log store.
            _coreConfig.log.access = null;
            _coreConfig.log.error = null;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }
}
