using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        
        // 初始化状态跟踪
        private volatile bool _initializationCompleted = false;
        private volatile bool _virtualDesktopSupported = false;
        private readonly object _initLock = new object();
        private string _initializationError = null;
        
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
            
            // 等待初始化完成
            WaitForInitialization();
            
            // 如果初始化失败或不支持虚拟桌面，返回null
            if (!_virtualDesktopSupported)
            {
                Logger.LogDebug($"[VirtualDesktopSynchronizer] 虚拟桌面不支持: {_initializationError ?? "未知原因"}");
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
                // 缓存过期或无效，重新获取（带超时保护）
                Logger.LogDebug("[VirtualDesktopSynchronizer] 刷新虚拟桌面缓存");
                
                var (desktops, current) = GetVirtualDesktopsWithTimeout();
                
                if (desktops == null)
                {
                    Logger.LogDebug("[VirtualDesktopSynchronizer] 获取虚拟桌面信息超时或失败");
                    return (null, null);
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
        /// 获取虚拟桌面信息 - STA线程兼容的同步版本
        /// </summary>
        private (IEnumerable<WindowsDesktop.VirtualDesktop> desktops, WindowsDesktop.VirtualDesktop current) GetVirtualDesktopsWithTimeout()
        {
            try
            {
                // STA线程修复：直接在当前线程获取，避免Task.Run创建MTA线程
                var desktops = WindowsDesktop.VirtualDesktop.GetDesktops();
                var current = WindowsDesktop.VirtualDesktop.Current;
                return (desktops, current);
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[VirtualDesktopSynchronizer] 获取虚拟桌面信息异常: {ex.Message}");
                return (null, null);
            }
        }
        
        /// <summary>
        /// 初始化虚拟桌面同步器 - STA线程兼容的同步初始化
        /// 修复：移除异步初始化以确保VirtualDesktop COM对象在STA线程上工作
        /// </summary>
        public VirtualDesktopSynchronizer()
        {
            Logger.Log("[VirtualDesktopSynchronizer] 初始化 Slions.VirtualDesktop 同步器...");
            
            // STA线程修复：移除异步初始化，改为同步初始化以确保VirtualDesktop库STA线程兼容性
            // 历史教训：异步Task.Run创建的MTA线程会导致VirtualDesktop API失效并引起程序崩溃
            InitializeSync();
        }
        
        /// <summary>
        /// 同步初始化虚拟桌面功能 - STA线程兼容版本
        /// </summary>
        private void InitializeSync()
        {
            try
            {
                Logger.Log("[VirtualDesktopSynchronizer] 🚀 开始同步初始化虚拟桌面功能...");
                Logger.Log($"[VirtualDesktopSynchronizer] 🔧 当前线程ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                Logger.Log($"[VirtualDesktopSynchronizer] 🔧 STA状态: {System.Threading.Thread.CurrentThread.GetApartmentState()}");
                
                Logger.Log("[VirtualDesktopSynchronizer] 🎯 直接在当前线程进行VirtualDesktop测试（STA兼容）...");
                // STA线程修复：直接在当前线程测试，避免Task.Run创建MTA线程
                _virtualDesktopSupported = TestVirtualDesktopSupportSync();
                Logger.Log($"[VirtualDesktopSynchronizer] 🔧 测试完成，结果: {_virtualDesktopSupported}");
                
                Logger.Log("[VirtualDesktopSynchronizer] 🔒 设置初始化完成状态...");
                lock (_initLock)
                {
                    _initializationCompleted = true;
                }
                
                if (_virtualDesktopSupported)
                {
                    Logger.Log("[VirtualDesktopSynchronizer] ✅ Slions.VirtualDesktop 库同步初始化成功 (STA线程兼容)");
                }
                else
                {
                    Logger.Log("[VirtualDesktopSynchronizer] ⚠️ 警告: Slions.VirtualDesktop 库不可用，将使用兼容模式");
                }
            }
            catch (Exception ex)
            {
                Logger.LogCritical($"VirtualDesktopSynchronizer 同步初始化严重失败: {ex.GetType().Name} - {ex.Message}");
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 同步初始化严重失败: {ex.GetType().Name}");
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 异常消息: {ex.Message}");
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 异常堆栈: {ex.StackTrace}");
                lock (_initLock)
                {
                    _initializationCompleted = true;
                    _virtualDesktopSupported = false;
                    _initializationError = ex.Message;
                }
                Logger.Log($"[VirtualDesktopSynchronizer] ⚠️ 同步初始化失败: {ex.Message}，将使用兼容模式");
            }
        }
        
        /// <summary>
        /// 测试虚拟桌面支持（带取消令牌）- 增强调试版本
        /// </summary>
        private bool TestVirtualDesktopSupport(CancellationToken cancellationToken)
        {
            try
            {
                Logger.Log("[VirtualDesktopSynchronizer] 🔍 开始测试虚拟桌面支持...");
                cancellationToken.ThrowIfCancellationRequested();
                
                Logger.Log("[VirtualDesktopSynchronizer] 🔍 步骤1: 尝试获取虚拟桌面列表...");
                // 尝试获取虚拟桌面信息
                var desktops = WindowsDesktop.VirtualDesktop.GetDesktops();
                Logger.Log("[VirtualDesktopSynchronizer] ✅ 步骤1: GetDesktops() 调用成功");
                cancellationToken.ThrowIfCancellationRequested();
                
                Logger.Log("[VirtualDesktopSynchronizer] 🔍 步骤2: 尝试获取当前虚拟桌面...");
                var current = WindowsDesktop.VirtualDesktop.Current;
                Logger.Log("[VirtualDesktopSynchronizer] ✅ 步骤2: Current 属性获取成功");
                cancellationToken.ThrowIfCancellationRequested();
                
                Logger.Log("[VirtualDesktopSynchronizer] 🔍 步骤3: 统计虚拟桌面数量...");
                int desktopCount = desktops?.Count() ?? 0;
                Logger.Log($"[VirtualDesktopSynchronizer] ✅ 步骤3: 检测到 {desktopCount} 个虚拟桌面");
                
                bool supported = desktopCount > 0;
                Logger.Log($"[VirtualDesktopSynchronizer] 🎯 虚拟桌面支持测试结果: {(supported ? "支持" : "不支持")}");
                
                return supported;
            }
            catch (OperationCanceledException ex)
            {
                Logger.Log($"[VirtualDesktopSynchronizer] ⏰ 虚拟桌面测试被取消: {ex.Message}");
                throw; // 重新抛出取消异常
            }
            catch (Exception ex)
            {
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 虚拟桌面检测失败: {ex.GetType().Name}");
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 异常消息: {ex.Message}");
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 异常堆栈: {ex.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// 测试虚拟桌面支持 - STA线程兼容的同步版本
        /// </summary>
        private bool TestVirtualDesktopSupportSync()
        {
            try
            {
                Logger.Log("[VirtualDesktopSynchronizer] 🔍 开始测试虚拟桌面支持（STA线程）...");
                
                Logger.Log("[VirtualDesktopSynchronizer] 🔍 步骤1: 尝试获取虚拟桌面列表...");
                // 尝试获取虚拟桌面信息
                var desktops = WindowsDesktop.VirtualDesktop.GetDesktops();
                Logger.Log("[VirtualDesktopSynchronizer] ✅ 步骤1: GetDesktops() 调用成功");
                
                Logger.Log("[VirtualDesktopSynchronizer] 🔍 步骤2: 尝试获取当前虚拟桌面...");
                var current = WindowsDesktop.VirtualDesktop.Current;
                Logger.Log("[VirtualDesktopSynchronizer] ✅ 步骤2: Current 属性获取成功");
                
                Logger.Log("[VirtualDesktopSynchronizer] 🔍 步骤3: 统计虚拟桌面数量...");
                int desktopCount = desktops?.Count() ?? 0;
                Logger.Log($"[VirtualDesktopSynchronizer] ✅ 步骤3: 检测到 {desktopCount} 个虚拟桌面");
                
                bool supported = desktopCount > 0;
                Logger.Log($"[VirtualDesktopSynchronizer] 🎯 虚拟桌面支持测试结果: {(supported ? "支持" : "不支持")} (STA线程兼容)");
                
                return supported;
            }
            catch (Exception ex)
            {
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 虚拟桌面检测失败: {ex.GetType().Name}");
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 异常消息: {ex.Message}");
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 异常堆栈: {ex.StackTrace}");
                return false;
            }
        }
        
        /// <summary>
        /// 检查虚拟桌面支持状态（等待异步初始化完成）
        /// </summary>
        /// <returns>是否支持虚拟桌面功能</returns>
        public bool IsVirtualDesktopSupported()
        {
            // 等待异步初始化完成
            WaitForInitialization();
            
            return _virtualDesktopSupported;
        }
        
        /// <summary>
        /// 等待初始化完成（最多等待10秒）
        /// </summary>
        private void WaitForInitialization()
        {
            const int maxWaitMs = 10000; // 10秒超时
            const int checkIntervalMs = 100;
            int totalWaitedMs = 0;
            
            while (!_initializationCompleted && totalWaitedMs < maxWaitMs)
            {
                Thread.Sleep(checkIntervalMs);
                totalWaitedMs += checkIntervalMs;
            }
            
            if (!_initializationCompleted)
            {
                Logger.Log("[VirtualDesktopSynchronizer] 等待初始化超时，强制使用兼容模式");
                lock (_initLock)
                {
                    _initializationCompleted = true;
                    _virtualDesktopSupported = false;
                    _initializationError = "等待初始化超时";
                }
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
        /// 同步壁纸到所有虚拟桌面 - 增强调试版本
        /// </summary>
        /// <param name="wallpaperPath">壁纸文件路径</param>
        /// <returns>同步是否成功</returns>
        public bool SynchronizeWallpaperToAllDesktops(string wallpaperPath)
        {
            Logger.Log("[VirtualDesktopSynchronizer] 🎯 ========== 开始虚拟桌面壁纸同步 ==========");
            Logger.Log($"[VirtualDesktopSynchronizer] 🔧 当前线程ID: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            
            if (disposed)
            {
                Logger.Log("[VirtualDesktopSynchronizer] ❌ 对象已释放，无法执行同步");
                return false;
            }
            
            Logger.Log("[VirtualDesktopSynchronizer] 🔍 步骤1: 验证壁纸路径...");
            if (string.IsNullOrEmpty(wallpaperPath))
            {
                Logger.Log("[VirtualDesktopSynchronizer] ❌ 壁纸路径为空，同步终止");
                return false;
            }
            Logger.Log($"[VirtualDesktopSynchronizer] 目标壁纸: {wallpaperPath}");
            
            Logger.Log("[VirtualDesktopSynchronizer] 🔍 步骤2: 检查壁纸文件存在性...");
            if (!System.IO.File.Exists(wallpaperPath))
            {
                Logger.Log("[VirtualDesktopSynchronizer] ❌ 壁纸文件不存在: " + wallpaperPath);
                return false;
            }
            Logger.Log("[VirtualDesktopSynchronizer] ✅ 壁纸文件存在");
            
            bool result = false;
            try
            {
                Logger.Log("[VirtualDesktopSynchronizer] 🔍 步骤3: 检查虚拟桌面支持状态...");
                // 优先尝试使用Slions.VirtualDesktop
                if (IsVirtualDesktopSupported())
                {
                    Logger.Log("[VirtualDesktopSynchronizer] ✅ 虚拟桌面支持，使用Slions.VirtualDesktop进行同步");
                    result = SyncWithSlionsVirtualDesktop(wallpaperPath);
                }
                else
                {
                    Logger.Log("[VirtualDesktopSynchronizer] ⚠️ 虚拟桌面不支持，使用兼容模式同步");
                    result = SyncWithSystemParametersInfo(wallpaperPath);
                }
                
                Logger.Log($"[VirtualDesktopSynchronizer] 🎯 同步操作完成，结果: {(result ? "成功" : "失败")}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.LogCritical($"VirtualDesktopSynchronizer 同步过程严重异常: {ex.GetType().Name} - {ex.Message}");
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 同步过程严重异常: {ex.GetType().Name}");
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 异常消息: {ex.Message}");
                Logger.Log($"[VirtualDesktopSynchronizer] ❌ 异常堆栈: {ex.StackTrace}");
                return false;
            }
            finally
            {
                Logger.Log("[VirtualDesktopSynchronizer] 🧹 开始清理COM对象...");
                // 同步完成后强制清理COM对象
                ForceCleanupComObjects();
                Logger.Log("[VirtualDesktopSynchronizer] 🎯 ========== 虚拟桌面壁纸同步流程结束 ==========");
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