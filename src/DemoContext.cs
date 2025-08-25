using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using WallpaperSync.Core;
using WallpaperSync.Services;
using WallpaperSync.UI;

namespace WallpaperSync
{
    /// <summary>
    /// 应用程序上下文 - 简化版本，只负责协调各层
    /// </summary>
    public class DemoContext : ApplicationContext
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ConfigService _configService;
        private readonly WallpaperService _wallpaperService;
        private readonly TrayController _trayController;
        private readonly System.Windows.Forms.Timer _monitoringTimer;
        private readonly System.Windows.Forms.Timer _performanceTimer;
        private bool _disposed = false;

        public DemoContext(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            Logger.LogStartup("WallpaperSync", "1.0 - 架构重构版");
            Logger.Log("[DemoContext] 开始初始化应用程序上下文");

            // 获取服务实例
            _configService = _serviceProvider.GetRequiredService<ConfigService>();
            
            // 日志级别初始化已移除 - 现在通过编译时Debug/Release控制
            
            _wallpaperService = _serviceProvider.GetRequiredService<WallpaperService>();
            _trayController = _serviceProvider.GetRequiredService<TrayController>();

            // 创建监控定时器 - 使用WinForms Timer确保在STA线程上执行
            _monitoringTimer = new System.Windows.Forms.Timer();
            _monitoringTimer.Tick += OnMonitoringTick;
            
            // 创建性能监控定时器 (30秒间隔)
            _performanceTimer = new System.Windows.Forms.Timer();
            _performanceTimer.Interval = 30000;
            _performanceTimer.Tick += OnPerformanceTick;
            _performanceTimer.Enabled = true;

            // 启动壁纸监控
            StartWallpaperMonitoring();

            Logger.Log("[DemoContext] 应用程序初始化完成");
        }

        private void StartWallpaperMonitoring()
        {
            var interval = _configService.MonitoringInterval;
            _monitoringTimer.Interval = interval;
            _monitoringTimer.Enabled = true;
            Logger.Log($"[DemoContext] 壁纸监控已启动，间隔: {interval}ms");
        }

        private void OnMonitoringTick(object sender, EventArgs e)
        {
            try
            {
                // 检查壁纸变更
                _wallpaperService.CheckForWallpaperChange();
                
                // 检查配置是否发生变化，如有变化才调整定时器
                var currentInterval = _configService.MonitoringInterval;
                if (_monitoringTimer.Interval != currentInterval)
                {
                    _monitoringTimer.Interval = currentInterval;
                    Logger.LogDebug($"[DemoContext] 监控间隔已调整为: {currentInterval}ms");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[DemoContext] 监控过程异常: {ex.Message}");
            }
        }

        private void OnPerformanceTick(object sender, EventArgs e)
        {
            try
            {
                Logger.LogPerformanceData();
            }
            catch (Exception ex)
            {
                Logger.Log($"[DemoContext] 性能监控异常: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                Logger.Log("[DemoContext] 开始释放应用程序资源");

                // 释放应用级定时器
                _monitoringTimer?.Dispose();
                _performanceTimer?.Dispose();

                // 统一通过服务容器释放所有服务，避免手动管理
                // 服务容器会自动处理所有实现 IDisposable 的注册服务
                if (_serviceProvider is IDisposable disposableProvider)
                {
                    Logger.LogDebug("[DemoContext] 通过服务容器统一释放所有服务资源");
                    disposableProvider.Dispose();
                }

                Logger.Log("[DemoContext] 应用程序资源释放完成");
                _disposed = true;
            }

            base.Dispose(disposing);
        }
    }
}