using System;
using Microsoft.Win32;
using WallpaperSync.Core;

namespace WallpaperSync.Services
{
    public class WallpaperService : IDisposable
    {
        private VirtualDesktopSynchronizer _synchronizer = null; // 延迟加载
        private readonly PerformanceService _performanceService;
        private string _lastWallpaperPath = "";
        private readonly object _synchronizerLock = new object(); // 线程安全保护
        
        // 壁纸同步事件
        public event Action<string, bool> WallpaperSyncCompleted;

        public WallpaperService(PerformanceService performanceService)
        {
            _performanceService = performanceService;
            _lastWallpaperPath = GetCurrentWallpaperFromRegistry();
            Logger.Log($"[WallpaperService] 初始化完成，当前壁纸: {_lastWallpaperPath}");
            Logger.LogDebug("[WallpaperService] VirtualDesktopSynchronizer采用延迟加载策略");
        }

        /// <summary>
        /// 延迟获取VirtualDesktopSynchronizer实例，减少启动时内存占用
        /// </summary>
        private VirtualDesktopSynchronizer GetSynchronizer()
        {
            if (_synchronizer == null)
            {
                lock (_synchronizerLock)
                {
                    if (_synchronizer == null)
                    {
                        Logger.LogDebug("[WallpaperService] 首次使用，正在创建VirtualDesktopSynchronizer");
                        _synchronizer = new VirtualDesktopSynchronizer();
                    }
                }
            }
            return _synchronizer;
        }

        public bool ManualSync()
        {
            try
            {
                Logger.Log("[WallpaperService] 执行手动同步");
                var currentPath = GetCurrentWallpaperFromRegistry();
                
                if (string.IsNullOrEmpty(currentPath))
                {
                    Logger.Log("[WallpaperService] 无法获取当前壁纸路径");
                    return false;
                }

                var result = GetSynchronizer().SynchronizeWallpaperToAllDesktops(currentPath);
                
                // 🚨 紧急修复：手动同步成功后，立即更新 _lastWallpaperPath
                // 防止自动监控循环检测到"变化"并重复同步旧壁纸
                if (result)
                {
                    _lastWallpaperPath = currentPath;
                    Logger.LogDebug($"[WallpaperService] 手动同步后更新状态路径: {currentPath}");
                }
                
                Logger.Log($"[WallpaperService] 手动同步{(result ? "成功" : "失败")}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Log($"[WallpaperService] 手动同步异常: {ex.Message}");
                return false;
            }
        }

        public string GetCurrentWallpaperPath()
        {
            return GetCurrentWallpaperFromRegistry();
        }

        public bool CheckForWallpaperChange()
        {
            var startTime = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                var currentPath = GetCurrentWallpaperFromRegistry();
                var changed = !string.IsNullOrEmpty(currentPath) && currentPath != _lastWallpaperPath;
                
                if (changed)
                {
                    Logger.Log($"[WallpaperService] 检测到壁纸变更");
                    Logger.Log($"[WallpaperService] 旧路径: {_lastWallpaperPath}");
                    Logger.Log($"[WallpaperService] 新路径: {currentPath}");
                    _lastWallpaperPath = currentPath;
                    
                    // 自动同步
                    var syncResult = GetSynchronizer().SynchronizeWallpaperToAllDesktops(currentPath);
                    Logger.Log($"[WallpaperService] 自动同步{(syncResult ? "成功" : "失败")}");
                    
                    // 触发同步完成事件
                    WallpaperSyncCompleted?.Invoke(currentPath, syncResult);
                }

                return changed;
            }
            finally
            {
                startTime.Stop();
                _performanceService.RecordQueryTime(startTime.ElapsedMilliseconds);
            }
        }

        private string GetCurrentWallpaperFromRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    return key?.GetValue("Wallpaper")?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[WallpaperService] 读取注册表失败: {ex.Message}");
                return "";
            }
        }

        public string GetVirtualDesktopInfo()
        {
            var info = GetSynchronizer().GetVirtualDesktopInfo();
            return info.ToString();
        }

        #region IDisposable Implementation
        
        private bool _disposed = false;
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                Logger.Log("[WallpaperService] 开始释放资源");
                
                // 释放VirtualDesktopSynchronizer (如果已创建)
                lock (_synchronizerLock)
                {
                    _synchronizer?.Dispose();
                    _synchronizer = null;
                    Logger.LogDebug("[WallpaperService] VirtualDesktopSynchronizer已释放");
                }
                
                Logger.Log("[WallpaperService] 资源释放完成");
                _disposed = true;
            }
        }
        
        #endregion

        public bool SetWallpaperForAllDesktops(string wallpaperPath)
        {
            try
            {
                Logger.Log($"[WallpaperService] 批量设置壁纸: {wallpaperPath}");
                var result = GetSynchronizer().SynchronizeWallpaperToAllDesktops(wallpaperPath);
                Logger.Log($"[WallpaperService] 批量设置{(result ? "成功" : "失败")}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Log($"[WallpaperService] 批量设置壁纸异常: {ex.Message}");
                return false;
            }
        }
    }
}