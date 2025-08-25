using System;
using WallpaperSync.Core;

namespace WallpaperSync.Services
{
    public class StartupService
    {
        private readonly StartupManager _startupManager;

        public StartupService()
        {
            _startupManager = new StartupManager();
        }

        public bool IsEnabled => _startupManager.IsStartupEnabled();

        public bool ToggleStartup()
        {
            try
            {
                if (IsEnabled)
                {
                    var result = _startupManager.DisableStartup();
                    Logger.Log($"[StartupService] 禁用开机自启: {(result ? "成功" : "失败")}");
                    return result;
                }
                else
                {
                    var result = _startupManager.EnableStartup();
                    Logger.Log($"[StartupService] 启用开机自启: {(result ? "成功" : "失败")}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[StartupService] 切换开机自启失败: {ex.Message}");
                return false;
            }
        }

        public string GetStatusText()
        {
            return "开机时启动";
        }
    }
}