using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using WallpaperSync.Core;

namespace WallpaperSync.Services
{
    public class PerformanceService : IDisposable
    {
        private readonly Queue<long> _queryTimes = new Queue<long>();
        private readonly int _sampleSize;
        private int _totalQueries = 0;
        private readonly object _lockObject = new object();
        
        // Phase 5a-1.5: 内存监控与诊断增强
        private readonly Queue<MemorySnapshot> _memoryHistory = new Queue<MemorySnapshot>();
        private readonly int _memoryHistorySize = 20; // 保留最近20次内存快照
        private DateTime _lastMemorySnapshot = DateTime.MinValue;
        private long _baselineMemoryMB = 0;
        private readonly object _memoryLock = new object();
        
        // WMI资源管理 - Phase 5a优化：移除单例连接，改为按需短连接
        private DateTime _lastSystemResourceLog = DateTime.MinValue;

        public PerformanceService(int sampleSize = 50)
        {
            _sampleSize = sampleSize;
            
            // Phase 5a优化：移除WMI单例连接初始化，改为按需创建
            Logger.LogDebug("[PerformanceService] WMI资源采用按需短连接策略，避免长期占用");
            
            // Phase 5a-1.5: 初始化内存监控基线
            InitializeMemoryBaseline();
        }

        /// <summary>
        /// 初始化内存监控基线 - Phase 5a-1.5 内存监控增强
        /// </summary>
        private void InitializeMemoryBaseline()
        {
            try
            {
                using (var currentProcess = System.Diagnostics.Process.GetCurrentProcess())
                {
                    _baselineMemoryMB = currentProcess.WorkingSet64 / (1024 * 1024);
                    Logger.LogDebug($"[PerformanceService] 内存监控基线设定: {_baselineMemoryMB}MB");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[PerformanceService] 内存基线初始化失败: {ex.Message}");
                _baselineMemoryMB = 50; // 默认基线50MB
            }
        }

        public void RecordQueryTime(long milliseconds)
        {
            ThrowIfDisposed();
            lock (_lockObject)
            {
                _queryTimes.Enqueue(milliseconds);
                _totalQueries++;

                // 保持样本大小
                if (_queryTimes.Count > _sampleSize)
                {
                    _queryTimes.Dequeue();
                }

                // 移除无意义的查询性能统计（都是0ms）
                // if (_totalQueries % 10 == 0)
                // {
                //     LogPerformanceStats();
                // }
                
                // 每30秒记录一次系统资源使用情况（仅在启用监控时）
#if ENABLE_PERFORMANCE_MONITORING
                if (DateTime.Now - _lastSystemResourceLog > TimeSpan.FromSeconds(30))
                {
                    LogSystemResourceUsage();
                    _lastSystemResourceLog = DateTime.Now;
                    
                    // Phase 5a-1.5: 同时执行内存监控
                    PerformMemoryMonitoring();
                }
#endif
            }
        }

        public double GetAverageQueryTime()
        {
            ThrowIfDisposed();
            lock (_lockObject)
            {
                if (_queryTimes.Count == 0) return 0;
                
                try
                {
                    return _queryTimes.Average();
                }
                catch
                {
                    return 0; // 异常时返回0，避免崩溃
                }
            }
        }

        public string GetPerformanceInfo()
        {
            lock (_lockObject)
            {
                if (_queryTimes.Count == 0)
                    return "性能统计: 无数据";

                try
                {
                    // 创建集合副本，避免在计算过程中被修改
                    var snapshot = _queryTimes.ToArray();
                    if (snapshot.Length == 0)
                        return "性能统计: 无数据";

                    var avg = snapshot.Average();
                    var min = snapshot.Min();
                    var max = snapshot.Max();

                    return $"查询性能 - 平均: {avg:F1}ms, 最小: {min}ms, 最大: {max}ms (样本: {snapshot.Length})";
                }
                catch
                {
                    return "性能统计: 计算异常";
                }
            }
        }

        private void LogPerformanceStats()
        {
            Logger.Log($"[性能监控] {GetPerformanceInfo()} - 总查询次数: {_totalQueries}");
        }

        public void Reset()
        {
            lock (_lockObject)
            {
                _queryTimes.Clear();
                _totalQueries = 0;
                Logger.Log("[PerformanceService] 性能统计已重置");
            }
        }

        /// <summary>
        /// 记录系统资源使用情况到日志（仅在启用监控时编译）
        /// 添加定期GC回收减少内存泄漏
        /// </summary>
        private void LogSystemResourceUsage()
        {
#if ENABLE_PERFORMANCE_MONITORING
            try
            {
                var memoryInfo = GetMemoryUsage();
                var processInfo = GetCurrentProcessMemory();
                Logger.Log($"[系统资源] {memoryInfo} | {processInfo}");
                
                // 每10次系统资源监控后执行一次轻量GC回收
                // 减少频繁GC的性能影响，同时清理WMI相关资源
                _totalQueries++;
                if (_totalQueries % 10 == 0)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                    Logger.LogDebug("[PerformanceService] 执行轻量GC回收，清理WMI资源累积");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[系统资源] 获取资源信息失败: {ex.Message}");
            }
#endif
        }

        // Phase 5a优化：移除GetOrCreateWmiScope方法，不再需要单例连接管理

        /// <summary>
        /// 获取系统内存使用情况 - Phase 5a优化：按需短连接，避免资源泄漏
        /// </summary>
        public string GetMemoryUsage()
        {
            ThrowIfDisposed();
            
            // Phase 5a WMI优化：不再依赖单例连接，每次查询使用独立连接
            ManagementScope scope = null;
            try
            {
                // 关键：确保在原有调用线程上执行，不改变线程上下文
                scope = new ManagementScope("\\\\localhost\\root\\cimv2");
                scope.Connect();
                
                var query = new ObjectQuery("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
                using var searcher = new ManagementObjectSearcher(scope, query);
                using var collection = searcher.Get();
                
                using var enumerator = collection.GetEnumerator();
                if (enumerator.MoveNext())
                {
                    using var memoryObj = (ManagementObject)enumerator.Current;
                    var totalMemoryKB = Convert.ToDouble(memoryObj["TotalVisibleMemorySize"]);
                    var freeMemoryKB = Convert.ToDouble(memoryObj["FreePhysicalMemory"]);

                    var totalMemoryMB = Math.Round(totalMemoryKB / 1024, 0);
                    var freeMemoryMB = Math.Round(freeMemoryKB / 1024, 0);
                    var usedMemoryMB = totalMemoryMB - freeMemoryMB;
                    var memoryUsagePercent = Math.Round((usedMemoryMB / totalMemoryMB) * 100, 1);

                    return $"系统内存: {usedMemoryMB}MB/{totalMemoryMB}MB ({memoryUsagePercent}%)"; 
                }
                
                return "系统内存: 无法获取数据";
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[PerformanceService] WMI查询异常: {ex.Message}");
                // WMI异常时自动降级到进程内存模式
                return GetCurrentProcessMemory().Replace("WallpaperSync进程", "系统内存(降级模式)");
            }
            finally
            {
                // ManagementScope不实现IDisposable，但我们确保连接用完即弃，让GC回收
                scope = null;
            }
        }

        /// <summary>
        /// 获取当前进程内存使用情况
        /// </summary>
        public string GetCurrentProcessMemory()
        {
            try
            {
                using (var currentProcess = System.Diagnostics.Process.GetCurrentProcess())
                {
                    var workingSetMB = Math.Round(currentProcess.WorkingSet64 / (1024.0 * 1024), 1);
                    var privateMemoryMB = Math.Round(currentProcess.PrivateMemorySize64 / (1024.0 * 1024), 1);

                    return $"WallpaperSync进程: 工作集{workingSetMB}MB, 私有内存{privateMemoryMB}MB";
                }
            }
            catch (Exception ex)
            {
                return $"WallpaperSync进程: 获取失败({ex.Message})";
            }
        }

        /// <summary>
        /// 手动触发系统资源监控并返回详细信息
        /// </summary>
        public string GetFullSystemResourceInfo()
        {
            var memoryInfo = GetMemoryUsage();
            var processInfo = GetCurrentProcessMemory();
            var queryInfo = GetPerformanceInfo();
            
            return $"[{DateTime.Now:HH:mm:ss}] {memoryInfo} | {processInfo} | {queryInfo}";
        }

        /// <summary>
        /// 创建内存快照 - Phase 5a-1.5 内存监控增强
        /// </summary>
        public MemorySnapshot CreateMemorySnapshot()
        {
            try
            {
                var snapshot = new MemorySnapshot
                {
                    Timestamp = DateTime.Now,
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2),
                    TotalMemoryBytes = GC.GetTotalMemory(false)
                };

                // 获取进程内存信息
                using (var currentProcess = System.Diagnostics.Process.GetCurrentProcess())
                {
                    snapshot.WorkingSetMB = currentProcess.WorkingSet64 / (1024 * 1024);
                    snapshot.PrivateMemoryMB = currentProcess.PrivateMemorySize64 / (1024 * 1024);
                }

                // 获取系统内存信息（尝试）
                try
                {
                    var systemMemoryInfo = GetSystemMemoryInfo();
                    snapshot.SystemUsedMemoryMB = systemMemoryInfo.UsedMB;
                    snapshot.SystemTotalMemoryMB = systemMemoryInfo.TotalMB;
                    snapshot.SystemMemoryPercent = systemMemoryInfo.UsagePercent;
                }
                catch
                {
                    // 系统内存信息获取失败时使用默认值
                    snapshot.SystemUsedMemoryMB = 0;
                    snapshot.SystemTotalMemoryMB = 0;
                    snapshot.SystemMemoryPercent = 0;
                }

                return snapshot;
            }
            catch (Exception ex)
            {
                Logger.Log($"[PerformanceService] 创建内存快照失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 记录内存快照到历史记录 - Phase 5a-1.5 内存监控增强
        /// </summary>
        private void RecordMemorySnapshot()
        {
            lock (_memoryLock)
            {
                var snapshot = CreateMemorySnapshot();
                if (snapshot != null)
                {
                    _memoryHistory.Enqueue(snapshot);
                    
                    // 保持历史记录大小限制
                    while (_memoryHistory.Count > _memoryHistorySize)
                    {
                        _memoryHistory.Dequeue();
                    }
                    
                    _lastMemorySnapshot = DateTime.Now;
                    Logger.LogDebug($"[PerformanceService] 内存快照已记录: {snapshot}");
                }
            }
        }

        /// <summary>
        /// 获取系统内存信息的简化版本 - Phase 5a优化：按需短连接
        /// </summary>
        private (long UsedMB, long TotalMB, double UsagePercent) GetSystemMemoryInfo()
        {
            // Phase 5a WMI优化：使用独立连接，避免依赖单例
            var scope = new ManagementScope("\\\\localhost\\root\\cimv2");
            scope.Connect();
            
            var query = new ObjectQuery("SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            using var searcher = new ManagementObjectSearcher(scope, query);
            using var collection = searcher.Get();
            
            using var enumerator = collection.GetEnumerator();
            if (enumerator.MoveNext())
            {
                using var memoryObj = (ManagementObject)enumerator.Current;
                var totalMemoryKB = Convert.ToDouble(memoryObj["TotalVisibleMemorySize"]);
                var freeMemoryKB = Convert.ToDouble(memoryObj["FreePhysicalMemory"]);

                var totalMemoryMB = (long)Math.Round(totalMemoryKB / 1024, 0);
                var freeMemoryMB = (long)Math.Round(freeMemoryKB / 1024, 0);
                var usedMemoryMB = totalMemoryMB - freeMemoryMB;
                var memoryUsagePercent = Math.Round((double)usedMemoryMB / totalMemoryMB * 100, 1);

                return (usedMemoryMB, totalMemoryMB, memoryUsagePercent);
            }
            
            throw new InvalidOperationException("无法获取系统内存信息");
        }

        /// <summary>
        /// 分析内存使用趋势 - Phase 5a-1.5 内存监控增强
        /// </summary>
        public string AnalyzeMemoryTrend()
        {
            lock (_memoryLock)
            {
                if (_memoryHistory.Count < 3)
                {
                    return "内存趋势分析: 数据不足 (需要至少3个样本点)";
                }

                var snapshots = _memoryHistory.ToArray();
                var latest = snapshots.Last();
                var earliest = snapshots.First();
                
                // 计算内存增长趋势
                var memoryGrowthMB = latest.WorkingSetMB - earliest.WorkingSetMB;
                var memoryGrowthPercent = Math.Round((double)memoryGrowthMB / earliest.WorkingSetMB * 100, 1);
                
                // 计算GC增长趋势
                var gcGrowth = (latest.Gen0Collections - earliest.Gen0Collections) + 
                              (latest.Gen1Collections - earliest.Gen1Collections) + 
                              (latest.Gen2Collections - earliest.Gen2Collections);
                
                // 内存泄漏检测
                var leakWarning = "";
                if (memoryGrowthMB > 50 && memoryGrowthPercent > 20)
                {
                    leakWarning = " ⚠️ 可能存在内存泄漏";
                }
                else if (memoryGrowthMB > _baselineMemoryMB * 2)
                {
                    leakWarning = " ⚠️ 内存使用超过基线2倍";
                }
                
                // 内存压力检测
                var pressureWarning = "";
                if (latest.WorkingSetMB > 200)
                {
                    pressureWarning = " ⚠️ 内存使用过高";
                }
                else if (latest.SystemMemoryPercent > 85)
                {
                    pressureWarning = " ⚠️ 系统内存压力过大";
                }

                return $"内存趋势 ({snapshots.Length}样本): 增长{memoryGrowthMB:+#;-#;0}MB ({memoryGrowthPercent:+#.#;-#.#;0}%), " +
                       $"GC总计: {gcGrowth}, 当前: {latest.WorkingSetMB}MB{leakWarning}{pressureWarning}";
            }
        }

        /// <summary>
        /// 获取详细内存诊断信息 - Phase 5a-1.5 内存监控增强
        /// </summary>
        public string GetMemoryDiagnosticInfo()
        {
            try
            {
                var snapshot = CreateMemorySnapshot();
                if (snapshot == null)
                {
                    return "内存诊断: 无法获取当前状态";
                }

                var trend = AnalyzeMemoryTrend();
                var gcInfo = $"GC统计: Gen0={snapshot.Gen0Collections}, Gen1={snapshot.Gen1Collections}, Gen2={snapshot.Gen2Collections}";
                var managedMemory = $"托管内存: {snapshot.TotalMemoryBytes / (1024 * 1024)}MB";
                var baselineInfo = $"相对基线: {snapshot.WorkingSetMB - _baselineMemoryMB:+#;-#;0}MB (基线: {_baselineMemoryMB}MB)";

                return $"[{DateTime.Now:HH:mm:ss}] 内存诊断报告\n" +
                       $"  当前状态: {snapshot}\n" +
                       $"  {trend}\n" +
                       $"  {gcInfo}, {managedMemory}\n" +
                       $"  {baselineInfo}";
            }
            catch (Exception ex)
            {
                return $"内存诊断失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 触发内存监控 - 在定期任务中调用
        /// </summary>
        public void PerformMemoryMonitoring()
        {
            // 每2分钟记录一次内存快照
            if (DateTime.Now - _lastMemorySnapshot > TimeSpan.FromMinutes(2))
            {
                RecordMemorySnapshot();
                
                // 检查是否需要内存警告
                var snapshot = CreateMemorySnapshot();
                if (snapshot?.WorkingSetMB > 150) // 超过150MB时警告
                {
                    Logger.Log($"[PerformanceService] ⚠️ 内存使用警告: {snapshot.WorkingSetMB}MB");
                    Logger.Log(AnalyzeMemoryTrend());
                }
            }
        }

        #region IDisposable Implementation

        private bool _disposed = false;

        /// <summary>
        /// 释放PerformanceService使用的资源，特别是ManagementScope连接
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Phase 5a优化：WMI资源现在使用按需短连接，无需手动清理单例连接
                    Logger.LogDebug("[PerformanceService] WMI按需连接模式，无需清理长期连接资源");

                    // 清理性能数据队列
                    lock (_lockObject)
                    {
                        _queryTimes.Clear();
                        Logger.Log("[PerformanceService] 性能数据队列已清理");
                    }
                    
                    // Phase 5a-1.5: 清理内存监控数据
                    lock (_memoryLock)
                    {
                        _memoryHistory.Clear();
                        Logger.LogDebug("[PerformanceService] 内存监控历史数据已清理");
                    }
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// 检查对象是否已被释放，如果已释放则抛出异常
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PerformanceService));
            }
        }

        #endregion
    }

    /// <summary>
    /// 内存快照数据结构 - Phase 5a-1.5 内存监控增强
    /// </summary>
    public class MemorySnapshot
    {
        public DateTime Timestamp { get; set; }
        public long WorkingSetMB { get; set; }
        public long PrivateMemoryMB { get; set; }
        public long SystemUsedMemoryMB { get; set; }
        public long SystemTotalMemoryMB { get; set; }
        public double SystemMemoryPercent { get; set; }
        
        // GC 统计信息
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
        public long TotalMemoryBytes { get; set; }
        
        public override string ToString()
        {
            return $"[{Timestamp:HH:mm:ss}] 进程: {WorkingSetMB}MB/{PrivateMemoryMB}MB, 系统: {SystemUsedMemoryMB}MB/{SystemTotalMemoryMB}MB ({SystemMemoryPercent:F1}%), GC: {Gen0Collections}/{Gen1Collections}/{Gen2Collections}";
        }
    }
}