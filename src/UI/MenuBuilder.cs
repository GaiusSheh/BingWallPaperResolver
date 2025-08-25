using System;
using System.Drawing;
using System.Windows.Forms;
using WallpaperSync.Core;
using WallpaperSync.Services;

namespace WallpaperSync.UI
{
    public class MenuBuilder : IDisposable
    {
        private readonly ConfigService _configService;
        private readonly StartupService _startupService;
        private readonly WallpaperService _wallpaperService;
        private readonly PerformanceService _performanceService;
        private readonly Action<string, string, ToolTipIcon> _showNotification;
        private CustomTrayMenu _customMenu;

        public MenuBuilder(ConfigService configService, StartupService startupService, 
                          WallpaperService wallpaperService, PerformanceService performanceService,
                          Action<string, string, ToolTipIcon> showNotification = null)
        {
            _configService = configService;
            _startupService = startupService;
            _wallpaperService = wallpaperService;
            _performanceService = performanceService;
            _showNotification = showNotification;
            Logger.Log("[MenuBuilder] 菜单构建器初始化完成");
        }

        public CustomTrayMenu CreateCustomTrayMenu()
        {
            // 如果已有菜单且被释放，重新创建
            if (_customMenu == null || _customMenu.IsDisposed)
            {
                // 释放旧菜单（如果存在）
                if (_customMenu != null && !_customMenu.IsDisposed)
                {
                    _customMenu.Dispose();
                }
                
                _customMenu = new CustomTrayMenu(_configService, _startupService, _wallpaperService, _performanceService, _showNotification);
                Logger.LogDebug("[MenuBuilder] 自定义托盘菜单创建完成");
            }
            return _customMenu;
        }

        public ContextMenuStrip CreateTrayMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new CustomMenuRenderer();
            
            // 关键修复：禁用ContextMenuStrip的自动尺寸调整
            menu.AutoSize = false;
            menu.Size = new Size(250, 400); // 设置菜单容器尺寸
            Logger.LogDebug($"[MenuBuilder] 设置菜单容器: AutoSize={menu.AutoSize}, Size={menu.Size}");
            
            // 禁用菜单项AutoSize以确保自定义尺寸生效
            menu.LayoutCompleted += (s, e) =>
            {
                Logger.LogDebug("[MenuBuilder] LayoutCompleted事件触发，开始设置菜单项尺寸");
                foreach (ToolStripItem item in menu.Items)
                {
                    if (item is ToolStripMenuItem menuItem)
                    {
                        Logger.LogDebug($"[MenuBuilder] 处理菜单项: {menuItem.Text}");
                        Logger.LogDebug($"[MenuBuilder] 原始AutoSize: {menuItem.AutoSize}, 原始尺寸: {menuItem.Size}");
                        
                        menuItem.AutoSize = false;
                        var newSize = new Size(400, 80); // 与容器尺寸匹配的更合理尺寸
                        menuItem.Size = newSize;
                        
                        Logger.LogDebug($"[MenuBuilder] 设置后AutoSize: {menuItem.AutoSize}, 设置的尺寸: {newSize}");
                        Logger.LogDebug($"[MenuBuilder] 实际尺寸: {menuItem.Size}, 是否有下拉: {menuItem.HasDropDownItems}");
                    }
                }
                Logger.LogDebug("[MenuBuilder] 所有菜单项尺寸设置完成");
            };
            
            // 也添加Opening事件来对比
            menu.Opening += (s, e) =>
            {
                Logger.LogDebug("[MenuBuilder] Opening事件触发，检查菜单项最终尺寸");
                foreach (ToolStripItem item in menu.Items)
                {
                    if (item is ToolStripMenuItem menuItem)
                    {
                        Logger.LogDebug($"[MenuBuilder] Opening中菜单项 {menuItem.Text}: AutoSize={menuItem.AutoSize}, Size={menuItem.Size}");
                    }
                }
            };

            // 强制同步
            var forceSync = new ToolStripMenuItem("强制同步");
            forceSync.Click += (s, e) =>
            {
                Logger.LogDebug("[MenuBuilder] 用户点击强制同步");
                _wallpaperService.ManualSync();
                MessageBox.Show("壁纸同步已执行完成", "WallpaperSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            menu.Items.Add(forceSync);

            // 监控间隔调整
            var monitoringMenu = new ToolStripMenuItem("调整监控间隔");
            var intervalText = $"当前: {_configService.GetIntervalDisplayText()}";
            var currentInterval = new ToolStripMenuItem(intervalText);
            currentInterval.Click += (s, e) =>
            {
                _configService.CycleToNextInterval();
                currentInterval.Text = $"当前: {_configService.GetIntervalDisplayText()}";
            };
            monitoringMenu.DropDownItems.Add(currentInterval);
            menu.Items.Add(monitoringMenu);
            
            // 日志级别调整功能已移除 - 现在通过编译时Debug/Release控制

            // 获取桌面信息
            var desktopInfo = new ToolStripMenuItem("获取桌面信息");
            desktopInfo.Click += (s, e) =>
            {
                Logger.LogDebug("[MenuBuilder] 用户点击获取桌面信息");
                var info = _wallpaperService.GetVirtualDesktopInfo();
                MessageBox.Show(info, "虚拟桌面信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            menu.Items.Add(desktopInfo);

            // 性能统计
            var performanceInfo = new ToolStripMenuItem("性能统计");
            performanceInfo.Click += (s, e) =>
            {
                Logger.LogDebug("[MenuBuilder] 用户点击性能统计");
                var stats = _performanceService.GetPerformanceInfo();
                MessageBox.Show(stats, "性能统计信息", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            menu.Items.Add(performanceInfo);

            // 开机自启
            var startupItem = new ToolStripMenuItem(_startupService.GetStatusText())
            {
                Checked = _startupService.IsEnabled
            };
            startupItem.Click += (s, e) =>
            {
                Logger.LogDebug("[MenuBuilder] 用户切换开机自启状态");
                var success = _startupService.ToggleStartup();
                if (success)
                {
                    startupItem.Checked = _startupService.IsEnabled;
                    startupItem.Text = _startupService.GetStatusText();
                }
            };
            menu.Items.Add(startupItem);

            // 分隔线
            menu.Items.Add(new ToolStripSeparator());

            // 退出
            var exit = new ToolStripMenuItem("退出");
            exit.Click += (s, e) =>
            {
                Logger.LogDebug("[MenuBuilder] 用户点击退出");
                Application.Exit();
            };
            menu.Items.Add(exit);

            Logger.LogDebug("[MenuBuilder] 托盘菜单创建完成");
            return menu;
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
                Logger.Log("[MenuBuilder] 开始释放菜单构建器资源");
                
                // 释放CustomTrayMenu
                if (_customMenu != null && !_customMenu.IsDisposed)
                {
                    _customMenu.Dispose();
                    _customMenu = null;
                }
                
                Logger.Log("[MenuBuilder] 菜单构建器资源释放完成");
                _disposed = true;
            }
        }
        
        #endregion
    }
}