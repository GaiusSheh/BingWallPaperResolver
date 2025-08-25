using System;
using Microsoft.Win32;
using WallpaperSync.Core;

namespace WallpaperSync.Services
{
    public class ConfigService
    {
        private const string RegistryKeyPath = @"SOFTWARE\WallpaperSync";
        
        public int MonitoringInterval { get; private set; } = 200; // 默认200ms
        public bool AutoSyncEnabled { get; private set; } = true;
        // LogLevel 已移除 - 现在通过编译时Debug/Release控制
        
        public ConfigService()
        {
            LoadSettings();
        }

        public void SetMonitoringInterval(int intervalMs)
        {
            if (intervalMs < 200 || intervalMs > 5000)
            {
                Logger.Log($"[ConfigService] 无效的监控间隔: {intervalMs}ms，保持当前值: {MonitoringInterval}ms");
                return;
            }

            var oldInterval = MonitoringInterval;
            MonitoringInterval = intervalMs;
            Logger.Log($"[ConfigService] 监控间隔已更改: {oldInterval}ms → {intervalMs}ms");
        }

        public void SetAutoSyncEnabled(bool enabled)
        {
            AutoSyncEnabled = enabled;
            Logger.Log($"[ConfigService] 自动同步已{(enabled ? "启用" : "禁用")}");
        }

        public string GetIntervalDisplayText()
        {
            return MonitoringInterval switch
            {
                200 => "实时监控 (200ms)",
                500 => "高频监控 (500ms)", 
                1000 => "中频监控 (1000ms)",
                2000 => "低频监控 (2000ms)",
                _ => $"自定义 ({MonitoringInterval}ms)"
            };
        }

        public void CycleToNextInterval()
        {
            MonitoringInterval = MonitoringInterval switch
            {
                200 => 500,
                500 => 1000,
                1000 => 2000,
                2000 => 200,
                _ => 200
            };
            Logger.Log($"[ConfigService] 监控间隔已切换至: {GetIntervalDisplayText()}");
        }

        // SetLogLevel 方法已删除 - 通过编译时Debug/Release控制

        // GetLogLevelDisplayText 方法已删除

        // ToggleLogLevel 方法已删除

        // InitializeLogLevel 方法已删除
        
        /// <summary>
        /// 从注册表加载设置
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                if (key != null)
                {
                    // 日志级别读取已移除 - 通过编译时Debug/Release控制
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[ConfigService] 加载设置失败: {ex.Message}，使用默认值");
            }
        }
        
        // SaveLogLevel 方法已删除 - 日志级别通过编译时控制
    }
}