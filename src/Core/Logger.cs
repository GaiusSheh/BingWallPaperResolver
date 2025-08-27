using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

namespace WallpaperSync.Core
{
    /// <summary>
    /// 现代化日志记录类
    /// </summary>
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public static class Logger
    {
        private static readonly string LogFilePath = GenerateLogFilePath();
        private static readonly Dictionary<string, DateTime> _lastLogTime = new Dictionary<string, DateTime>();
        private const int MAX_THROTTLE_ENTRIES = 100; // 限制字典大小
        private static readonly object _logLock = new object();
        private const long MaxLogFileSizeBytes = 10 * 1024 * 1024; // 10MB限制
        
        // 日志级别现在通过编译时控制：Debug构建包含所有级别，Release构建只有Info及以上
        
        /// <summary>
        /// 生成基于启动时间的日志文件路径
        /// </summary>
        private static string GenerateLogFilePath()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"wallpaper_{timestamp}.log";
            var logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            
            // 确保logs目录存在
            if (!Directory.Exists(logsDirectory))
            {
                Directory.CreateDirectory(logsDirectory);
            }
            
            return Path.Combine(logsDirectory, filename);
        }

        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            // 日志级别检查现在通过编译时控制，这里不再需要运行时检查

            lock (_logLock)
            {
                // 异常防洪机制：同类异常每分钟最多记录1次
                if (message.Contains("异常") && ShouldThrottleMessage(message))
                {
                    return;
                }

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var levelTag = level == LogLevel.Debug ? "[DEBUG] " : "";
                var logEntry = $"[{timestamp}] {levelTag}{message}";
                
                // 控制台输出
                Console.WriteLine(logEntry);
                
                // 文件输出
                try
                {
                    File.AppendAllText(LogFilePath, logEntry + Environment.NewLine);
                }
                catch 
                {
                    // 忽略文件写入错误
                }
            }
        }

        // 便捷方法
        public static void LogDebug(string message) 
        {
#if DEBUG
            Log(message, LogLevel.Debug);
#endif
        }
        public static void LogInfo(string message) => Log(message, LogLevel.Info);
        public static void LogWarning(string message) => Log(message, LogLevel.Warning);
        public static void LogError(string message) => Log(message, LogLevel.Error);
        
        /// <summary>
        /// 记录关键错误到Windows事件日志，用于硬崩溃场景的备份
        /// </summary>
        public static void LogCritical(string message)
        {
            // 先记录到普通日志
            Log($"🚨 CRITICAL: {message}", LogLevel.Error);
            
            // 尝试写入Windows事件日志作为备份
            try
            {
                using (var eventLog = new EventLog("Application"))
                {
                    eventLog.Source = "WallpaperSync";
                    eventLog.WriteEntry($"WallpaperSync Critical Error: {message}", EventLogEntryType.Error);
                }
            }
            catch (Exception ex)
            {
                // 即使事件日志失败，也不能影响主程序
                Log($"警告: 无法写入Windows事件日志: {ex.Message}");
            }
        }
        
        private static bool ShouldThrottleMessage(string message)
        {
            var messageKey = GetMessageKey(message);
            var now = DateTime.Now;
            
            if (_lastLogTime.ContainsKey(messageKey))
            {
                var timeSinceLastLog = now - _lastLogTime[messageKey];
                if (timeSinceLastLog.TotalMinutes < 1) // 1分钟内不重复记录
                {
                    return true;
                }
            }
            
            _lastLogTime[messageKey] = now;
            
            // 防止字典无限增长，更激进的清理策略
            if (_lastLogTime.Count > MAX_THROTTLE_ENTRIES)
            {
                // 移除超过5分钟的所有条目，减少内存占用
                var cutoffTime = DateTime.Now.AddMinutes(-5);
                var keysToRemove = _lastLogTime
                    .Where(kvp => kvp.Value < cutoffTime)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var key in keysToRemove)
                {
                    _lastLogTime.Remove(key);
                }
                
                // 如果清理后仍然过多，移除最旧的一半条目
                if (_lastLogTime.Count > MAX_THROTTLE_ENTRIES)
                {
                    var sortedEntries = _lastLogTime.OrderBy(kvp => kvp.Value).Take(_lastLogTime.Count / 2);
                    foreach (var entry in sortedEntries.ToList())
                    {
                        _lastLogTime.Remove(entry.Key);
                    }
                }
            }
            
            return false;
        }
        
        private static string GetMessageKey(string message)
        {
            // 提取异常的关键特征作为key，忽略具体细节
            if (message.Contains("Collection was modified"))
                return "collection_modified";
            if (message.Contains("Source array was not long enough"))
                return "array_length";
            if (message.Contains("监控过程异常"))
                return "monitor_exception";
            
            // 其他异常按前50个字符分类
            return message.Length > 50 ? message.Substring(0, 50) : message;
        }
        
        /// <summary>
        /// 清理超过7天的旧日志文件
        /// </summary>
        public static void CleanupOldLogFiles()
        {
            try
            {
                var logDir = AppDomain.CurrentDomain.BaseDirectory;
                var logFiles = Directory.GetFiles(logDir, "wallpaper_*.log");
                var cutoffDate = DateTime.Now.AddDays(-7);
                
                foreach (var logFile in logFiles)
                {
                    var fileInfo = new FileInfo(logFile);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        File.Delete(logFile);
                    }
                }
            }
            catch
            {
                // 忽略清理错误
            }
        }
        
        // 程序启动时的标准化日志头
        public static void LogStartup(string appName, string version)
        {
            // 启动时清理旧日志文件
            CleanupOldLogFiles();
            
            Log("==========================================");
            Log($"[{appName}] 应用程序启动");
            Log($"[{appName}] 当前时间: {DateTime.Now}");
            Log($"[{appName}] 程序版本: {version}");
            Log($"[{appName}] 日志文件: {Path.GetFileName(LogFilePath)}");
            Log("==========================================");
        }

        /// <summary>
        /// 获取当前日志文件完整路径（每次启动的唯一文件）
        /// </summary>
        public static string GetCurrentLogFileName()
        {
            return Path.GetFileName(LogFilePath);
        }

        public static string GetLogFilePath()
        {
            return LogFilePath;
        }

        // SetLogLevel 和 GetCurrentLogLevel 方法已删除 - 现在通过编译时控制

        // InitializeFromConfig 方法已删除 - 现在通过编译时控制

        /// <summary>
        /// 记录当前进程的性能数据 (CPU和内存)
        /// 简化版本，避免PerformanceCounter内存泄漏
        /// </summary>
        public static void LogPerformanceData()
        {
            try
            {
                using (var currentProcess = Process.GetCurrentProcess())
                {
                    var memoryMB = currentProcess.WorkingSet64 / (1024 * 1024); // 转换为MB
                    var privateMemoryMB = currentProcess.PrivateMemorySize64 / (1024 * 1024);
                    
                    // 移除CPU监控以避免PerformanceCounter内存泄漏
                    Log($"[性能监控] 工作集内存: {memoryMB} MB, 私有内存: {privateMemoryMB} MB");
                }
            }
            catch (Exception ex)
            {
                Log($"[性能监控] 获取性能数据失败: {ex.Message}");
            }
        }
    }
}