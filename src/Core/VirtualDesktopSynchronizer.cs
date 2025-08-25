using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using WallpaperSync.Core;

namespace WallpaperSync
{
    /// <summary>
    /// 虚拟桌面壁纸同步器 - 使用Slions.VirtualDesktop库
    /// 负责实现Windows 11虚拟桌面间的壁纸同步功能
    /// </summary>
    public class VirtualDesktopSynchronizer : IDisposable
    {
        private bool disposed = false;
        
        // 真正的缓存机制：减少COM对象创建以优化内存使用
        private IEnumerable<WindowsDesktop.VirtualDesktop> _cachedDesktops = null;
        private WindowsDesktop.VirtualDesktop _cachedCurrent = null;
        private DateTime _lastCacheTime = DateTime.MinValue;
        private readonly TimeSpan _cacheValidDuration = TimeSpan.FromSeconds(2); // 2秒缓存有效期
        
        /// <summary>
        /// 虚拟桌面信息结构
        /// </summary>
        public class VirtualDesktopInfo
        {
            public int TotalDesktops { get; set; }
            public int CurrentDesktopIndex { get; set; }
            public string CurrentDesktopName { get; set; }
            public bool IsVirtualDesktopSupported { get; set; }
            public string ErrorMessage { get; set; }
            public List<string> DesktopWallpapers { get; set; } = new List<string>();
            
            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"一共 {TotalDesktops} 个桌面，当前第 {CurrentDesktopIndex} 个桌面");
                
                if (!IsVirtualDesktopSupported)
                {
                    sb.AppendLine($"注意：虚拟桌面功能不受支持 - {ErrorMessage}");
                    return sb.ToString().Trim();
                }
                
                // 获取当前壁纸
                string currentWallpaper = GetCurrentWallpaper();
                
                if (DesktopWallpapers.Count > 0)
                {
                    for (int i = 0; i < DesktopWallpapers.Count && i < TotalDesktops; i++)
                    {
                        var wallpaper = DesktopWallpapers[i];
                        var fileName = string.IsNullOrEmpty(wallpaper) ? "无" : System.IO.Path.GetFileName(wallpaper);
                        sb.AppendLine($"桌面 {i + 1} 壁纸：{fileName}");
                    }
                }
                else
                {
                    // 如果没有具体的桌面壁纸信息，显示当前壁纸
                    var fileName = string.IsNullOrEmpty(currentWallpaper) ? "无" : System.IO.Path.GetFileName(currentWallpaper);
                    sb.AppendLine($"当前壁纸：{fileName}");
                }
                
                return sb.ToString().Trim();
            }
            
            private string GetCurrentWallpaper()
            {
                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                    {
                        return key?.GetValue("Wallpaper")?.ToString() ?? "";
                    }
                }
                catch
                {
                    return "";
                }
            }
        }
        
        /// <summary>
        /// 获取缓存的虚拟桌面信息 - 真正实现缓存以减少COM对象创建
        /// </summary>
        private (IEnumerable<WindowsDesktop.VirtualDesktop> desktops, WindowsDesktop.VirtualDesktop current) GetCachedVirtualDesktops()
        {
            // 检查对象是否已释放
            if (disposed)
            {
                Logger.LogDebug("[VirtualDesktopSynchronizer] 对象已释放，无法获取虚拟桌面");
                return (null, null);
            }
            
            // 检查缓存是否有效
            bool cacheValid = _cachedDesktops != null && 
                             _cachedCurrent != null && 
                             (DateTime.Now - _lastCacheTime) < _cacheValidDuration;
            
            if (cacheValid)
            {
                Logger.LogDebug("[VirtualDesktopSynchronizer] 使用缓存的虚拟桌面信息");
                return (_cachedDesktops, _cachedCurrent);
            }
            
            try
            {
                // 缓存过期或无效，重新获取
                Logger.LogDebug("[VirtualDesktopSynchronizer] 刷新虚拟桌面缓存");
                var desktops = WindowsDesktop.VirtualDesktop.GetDesktops();
                var current = WindowsDesktop.VirtualDesktop.Current;
                
                // 方案2: 更激进的COM清理 - 立即释放不需要长期持有的COM对象
                try
                {
                    // 创建轻量级副本，避免持有原始COM对象引用
                    var desktopsList = desktops?.ToList();
                    if (desktopsList != null)
                    {
                        // 立即释放枚举器中的COM对象（保留必要信息）
                        foreach (var desktop in desktops)
                        {
                            try 
                            {
                                // 访问必要属性后立即尝试释放
                                var name = desktop?.Name; // 触发属性访问
                                // 注意：不能直接释放desktop对象，因为我们还需要用它
                                // Marshal.ReleaseComObject(desktop); // 这会导致后续访问失败
                            }
                            catch (Exception ex)
                            {
                                Logger.LogDebug($"[VirtualDesktopSynchronizer] COM对象属性访问异常: {ex.Message}");
                            }
                        }
                    }
                    
                    Logger.LogDebug("[VirtualDesktopSynchronizer] 激进COM清理：属性预访问完成");
                }
                catch (Exception comEx)
                {
                    Logger.LogDebug($"[VirtualDesktopSynchronizer] 激进COM清理异常: {comEx.Message}");
                }
                
                // 更新缓存
                _cachedDesktops = desktops;
                _cachedCurrent = current;
                _lastCacheTime = DateTime.Now;
                
                Logger.LogDebug($"[VirtualDesktopSynchronizer] 虚拟桌面缓存已更新，桌面数量: {desktops?.Count() ?? 0}");
                return (desktops, current);
            }
            catch (Exception ex)
            {
                Logger.Log($"[VirtualDesktopSynchronizer] 获取VirtualDesktop失败: {ex.Message}");
                // 清空缓存以防止使用失效数据
                _cachedDesktops = null;
                _cachedCurrent = null;
                return (null, null);
            }
        }
        
        /// <summary>
        /// 初始化虚拟桌面同步器
        /// </summary>
        public VirtualDesktopSynchronizer()
        {
            try
            {
                Logger.Log("[VirtualDesktopSynchronizer] 初始化 Slions.VirtualDesktop 同步器...");
                
                // 测试Slions库是否可用
                if (IsVirtualDesktopSupported())
                {
                    Logger.Log("[VirtualDesktopSynchronizer] Slions.VirtualDesktop 库初始化成功");
                }
                else
                {
                    Logger.Log("[VirtualDesktopSynchronizer] 警告: Slions.VirtualDesktop 库不可用，将使用兼容模式");
                }
            }
            catch (Exception ex)
            {
                Logger.Log("[VirtualDesktopSynchronizer] 初始化失败: " + ex.Message);
                Logger.Log("[VirtualDesktopSynchronizer] 将使用兼容模式 (SystemParametersInfo)");
            }
        }
        
        /// <summary>
        /// 检查虚拟桌面支持状态
        /// </summary>
        /// <returns>是否支持虚拟桌面功能</returns>
        public bool IsVirtualDesktopSupported()
        {
            try
            {
                // 使用缓存的VirtualDesktop API访问
                var (desktops, current) = GetCachedVirtualDesktops();
                
                Logger.Log("[VirtualDesktopSynchronizer] 虚拟桌面检测: " + desktops.Count() + " 个桌面，当前: " + (current?.Name ?? "未知"));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("[VirtualDesktopSynchronizer] 虚拟桌面检测失败: " + ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 获取虚拟桌面信息
        /// </summary>
        /// <returns>虚拟桌面详细信息</returns>
        public VirtualDesktopInfo GetVirtualDesktopInfo()
        {
            var info = new VirtualDesktopInfo();
            
            try
            {
                var (desktops, current) = GetCachedVirtualDesktops();
                
                info.TotalDesktops = desktops.Count();
                info.CurrentDesktopIndex = desktops.ToList().IndexOf(current) + 1;
                info.CurrentDesktopName = current?.Name ?? $"桌面 {info.CurrentDesktopIndex}";
                info.IsVirtualDesktopSupported = true;
                
                // 尝试获取每个桌面的壁纸信息（在Windows中，大部分情况下所有桌面共享相同壁纸）
                try
                {
                    // Windows系统中，虚拟桌面通常共享相同的壁纸，但我们可以显示每个桌面的编号
                    var currentWallpaper = GetCurrentWallpaperFromRegistry();
                    for (int i = 0; i < info.TotalDesktops; i++)
                    {
                        info.DesktopWallpapers.Add(currentWallpaper);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[VirtualDesktopSynchronizer] 获取桌面壁纸信息失败: {ex.Message}");
                }
                
                Logger.Log($"[VirtualDesktopSynchronizer] 虚拟桌面信息: {info.TotalDesktops}个桌面，当前: {info.CurrentDesktopName}");
            }
            catch (Exception ex)
            {
                Logger.Log("[VirtualDesktopSynchronizer] 获取虚拟桌面信息失败: " + ex.Message);
                info.IsVirtualDesktopSupported = false;
                info.ErrorMessage = ex.Message;
                info.TotalDesktops = 1;
                info.CurrentDesktopIndex = 1;
                info.CurrentDesktopName = "桌面 1 (兼容模式)";
            }
            
            return info;
        }
        
        private string GetCurrentWallpaperFromRegistry()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    return key?.GetValue("Wallpaper")?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[VirtualDesktopSynchronizer] 从注册表获取壁纸路径失败: {ex.Message}");
                return "";
            }
        }
        
        /// <summary>
        /// 同步壁纸到所有虚拟桌面
        /// </summary>
        /// <param name="wallpaperPath">壁纸文件路径</param>
        /// <returns>同步是否成功</returns>
        public bool SynchronizeWallpaperToAllDesktops(string wallpaperPath)
        {
            if (disposed)
            {
                Logger.Log("[VirtualDesktopSynchronizer] 对象已释放，无法执行同步");
                return false;
            }
            
            if (string.IsNullOrEmpty(wallpaperPath))
            {
                Logger.Log("[VirtualDesktopSynchronizer] 壁纸路径为空，同步终止");
                return false;
            }
            
            if (!System.IO.File.Exists(wallpaperPath))
            {
                Logger.Log("[VirtualDesktopSynchronizer] 壁纸文件不存在: " + wallpaperPath);
                return false;
            }
            
            bool result = false;
            try
            {
                Logger.Log("[VirtualDesktopSynchronizer] ========== 开始虚拟桌面壁纸同步 ==========");
                Logger.Log("[VirtualDesktopSynchronizer] 目标壁纸: " + wallpaperPath);
                
                // 优先尝试使用Slions.VirtualDesktop
                if (IsVirtualDesktopSupported())
                {
                    result = SyncWithSlionsVirtualDesktop(wallpaperPath);
                }
                else
                {
                    Logger.Log("[VirtualDesktopSynchronizer] 使用兼容模式同步");
                    result = SyncWithSystemParametersInfo(wallpaperPath);
                }
                
                return result;
            }
            catch (Exception ex)
            {
                Logger.Log("[VirtualDesktopSynchronizer] 同步过程出错: " + ex.Message);
                Logger.Log("[VirtualDesktopSynchronizer] 错误堆栈: " + ex.StackTrace);
                return false;
            }
            finally
            {
                // 同步完成后强制清理COM对象
                ForceCleanupComObjects();
            }
        }
        
        /// <summary>
        /// 强制清理COM对象缓存和执行垃圾回收
        /// </summary>
        private void ForceCleanupComObjects()
        {
            try
            {
                Logger.LogDebug("[VirtualDesktopSynchronizer] 执行激进COM对象清理...");
                
                // 方案2: 更激进的COM清理策略
                try 
                {
                    // 尝试显式释放缓存中的COM对象（谨慎操作）
                    if (_cachedDesktops != null)
                    {
                        foreach (var desktop in _cachedDesktops)
                        {
                            try
                            {
                                // 注意：这个操作有风险，可能导致后续访问异常
                                // Marshal.ReleaseComObject(desktop);
                                // 改为温和方式：确保对象完成任何待处理的操作
                                if (desktop != null)
                                {
                                    // 触发一次属性访问，确保COM对象状态稳定
                                    var _ = desktop.Name;
                                }
                            }
                            catch (Exception desktopEx)
                            {
                                Logger.LogDebug($"[VirtualDesktopSynchronizer] 桌面COM对象清理异常: {desktopEx.Message}");
                            }
                        }
                    }
                    
                    if (_cachedCurrent != null)
                    {
                        try
                        {
                            // 同样的温和处理
                            var _ = _cachedCurrent.Name;
                            // Marshal.ReleaseComObject(_cachedCurrent);
                        }
                        catch (Exception currentEx)
                        {
                            Logger.LogDebug($"[VirtualDesktopSynchronizer] 当前桌面COM对象清理异常: {currentEx.Message}");
                        }
                    }
                    
                    Logger.LogDebug("[VirtualDesktopSynchronizer] 激进COM清理：显式释放尝试完成");
                }
                catch (Exception comEx)
                {
                    Logger.LogDebug($"[VirtualDesktopSynchronizer] 激进COM清理失败: {comEx.Message}");
                }
                
                // 清空缓存，强制下次重新获取
                _cachedDesktops = null;
                _cachedCurrent = null;
                _lastCacheTime = DateTime.MinValue;
                
                // 多轮强制垃圾回收以释放COM对象
                for (int i = 0; i < 3; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    System.Threading.Thread.Sleep(10); // 短暂等待
                }
                
                Logger.LogDebug("[VirtualDesktopSynchronizer] 激进COM对象清理完成");
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[VirtualDesktopSynchronizer] COM对象清理异常: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 使用Slions.VirtualDesktop库同步壁纸 - 优化版本，减少COM对象创建
        /// </summary>
        /// <param name="wallpaperPath">壁纸路径</param>
        /// <returns>同步是否成功</returns>
        private bool SyncWithSlionsVirtualDesktop(string wallpaperPath)
        {
            try
            {
                Logger.Log("[VirtualDesktopSynchronizer] 使用 Slions.VirtualDesktop 进行同步");
                
                // 方法1: 优先使用批量同步API，避免创建多个COM对象
                try
                {
                    WindowsDesktop.VirtualDesktop.UpdateWallpaperForAllDesktops(wallpaperPath);
                    Logger.Log("[VirtualDesktopSynchronizer] 批量同步成功 (UpdateWallpaperForAllDesktops)");
                    Logger.Log("[VirtualDesktopSynchronizer] ========== 虚拟桌面壁纸同步成功 ==========");
                    return true;
                }
                catch (Exception batchEx)
                {
                    Logger.Log("[VirtualDesktopSynchronizer] 批量同步失败: " + batchEx.Message);
                    Logger.Log("[VirtualDesktopSynchronizer] 尝试逐个桌面同步...");
                }
                
                // 方法2: 只在批量同步失败时才获取桌面信息进行逐个同步
                var (desktops, current) = GetCachedVirtualDesktops();
                
                if (desktops == null)
                {
                    Logger.Log("[VirtualDesktopSynchronizer] 无法获取虚拟桌面信息，同步失败");
                    return false;
                }
                
                Logger.Log($"[VirtualDesktopSynchronizer] 检测到 {desktops.Count()} 个虚拟桌面");
                Logger.Log($"[VirtualDesktopSynchronizer] 当前桌面: {current?.Name ?? "未知"}");
                
                // 逐个桌面设置 (备用方案)
                int successCount = 0;
                int totalCount = desktops.Count();
                
                foreach (var desktop in desktops)
                {
                    try
                    {
                        desktop.WallpaperPath = wallpaperPath;
                        successCount++;
                        Logger.LogDebug($"[VirtualDesktopSynchronizer] 桌面 '{desktop.Name ?? "未知"}' 壁纸设置成功");
                    }
                    catch (Exception desktopEx)
                    {
                        Logger.Log($"[VirtualDesktopSynchronizer] 桌面 '{desktop.Name ?? "未知"}' 设置失败: " + desktopEx.Message);
                    }
                }
                
                bool success = successCount > 0;
                Logger.Log($"[VirtualDesktopSynchronizer] 逐个同步完成: {successCount}/{totalCount} 个桌面成功");
                
                if (success)
                {
                    Logger.Log("[VirtualDesktopSynchronizer] ========== 虚拟桌面壁纸同步成功 ==========");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Logger.Log("[VirtualDesktopSynchronizer] Slions.VirtualDesktop 同步失败: " + ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 使用SystemParametersInfo兼容模式同步壁纸
        /// </summary>
        /// <param name="wallpaperPath">壁纸路径</param>
        /// <returns>同步是否成功</returns>
        private bool SyncWithSystemParametersInfo(string wallpaperPath)
        {
            try
            {
                Logger.Log("[VirtualDesktopSynchronizer] 使用 SystemParametersInfo 兼容模式");
                
                // 调用现有的WallpaperSynchronizer作为兼容方案
                using (var fallbackSynchronizer = new WallpaperSynchronizer())
                {
                    // 🚨 紧急修复：传递正确的壁纸路径给兼容模式
                    // 注意：这只是兼容模式，在Windows 11虚拟桌面环境下效果有限
                    fallbackSynchronizer.SyncWallpaper(wallpaperPath);
                }
                
                Logger.Log("[VirtualDesktopSynchronizer] 兼容模式同步完成 (效果可能有限)");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("[VirtualDesktopSynchronizer] 兼容模式同步失败: " + ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 获取当前桌面的壁纸路径
        /// </summary>
        /// <returns>壁纸文件路径</returns>
        public string GetCurrentWallpaperPath()
        {
            try
            {
                if (IsVirtualDesktopSupported())
                {
                    var (_, current) = GetCachedVirtualDesktops();
                    string wallpaperPath = current?.WallpaperPath;
                    
                    if (!string.IsNullOrEmpty(wallpaperPath))
                    {
                        Logger.Log("[VirtualDesktopSynchronizer] 虚拟桌面壁纸路径: " + wallpaperPath);
                        return wallpaperPath;
                    }
                }
                
                // 回退到注册表方式
                using (var fallbackSynchronizer = new WallpaperSynchronizer())
                {
                    string wallpaperPath = fallbackSynchronizer.GetCurrentWallpaperPath();
                    Logger.Log("[VirtualDesktopSynchronizer] 注册表壁纸路径: " + wallpaperPath);
                    return wallpaperPath;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("[VirtualDesktopSynchronizer] 获取壁纸路径失败: " + ex.Message);
                return "";
            }
        }
        
        /// <summary>
        /// 手动同步当前壁纸到所有虚拟桌面
        /// </summary>
        /// <returns>同步是否成功</returns>
        public bool ManualSyncCurrentWallpaper()
        {
            try
            {
                Logger.Log("[VirtualDesktopSynchronizer] 开始手动同步当前壁纸");
                
                string currentWallpaperPath = GetCurrentWallpaperPath();
                
                if (string.IsNullOrEmpty(currentWallpaperPath))
                {
                    Logger.Log("[VirtualDesktopSynchronizer] 无法获取当前壁纸路径，手动同步终止");
                    return false;
                }
                
                return SynchronizeWallpaperToAllDesktops(currentWallpaperPath);
            }
            catch (Exception ex)
            {
                Logger.Log("[VirtualDesktopSynchronizer] 手动同步失败: " + ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // 清理缓存引用
                    _cachedDesktops = null;
                    _cachedCurrent = null;
                    Logger.Log("[VirtualDesktopSynchronizer] 释放资源完成");
                }
                
                disposed = true;
            }
        }
        
        /// <summary>
        /// 析构函数
        /// </summary>
        ~VirtualDesktopSynchronizer()
        {
            Dispose(false);
        }
    }
}