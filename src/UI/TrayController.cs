using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using WallpaperSync.Core;
using WallpaperSync.Services;

namespace WallpaperSync.UI
{
    public class TrayController : IDisposable
    {
        private readonly NotifyIcon _trayIcon;
        private readonly MenuBuilder _menuBuilder;
        private bool _disposed = false;
        private string _iconPath;

        public TrayController(ConfigService configService, StartupService startupService,
                             WallpaperService wallpaperService, PerformanceService performanceService)
        {
            Logger.Log("[TrayController] 初始化托盘控制器");

            // 通过依赖注入获取MenuBuilder，但仍需传递回调函数
            // 注意：由于回调函数在构造时需要传递，暂时保持直接创建方式
            // TODO: 未来可考虑通过事件系统或其他方式解耦回调依赖
            _menuBuilder = new MenuBuilder(configService, startupService, wallpaperService, performanceService, ShowNotificationCallback);
            
            // 订阅壁纸同步完成事件
            wallpaperService.WallpaperSyncCompleted += OnWallpaperSyncCompleted;
            
            _trayIcon = new NotifyIcon
            {
                Icon = LoadCustomIcon() ?? SystemIcons.Application,
                Text = "WallpaperSync - Windows虚拟桌面壁纸同步工具",
                Visible = true
                // 移除原有的ContextMenuStrip，改为使用自定义菜单
            };
            
            // 监听显示设置变更
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

            // 右键点击事件 - 显示自定义菜单
            _trayIcon.MouseClick += OnTrayIconClick;
            // 双击事件
            _trayIcon.MouseDoubleClick += OnTrayIconDoubleClick;
            
            Logger.Log("[TrayController] 托盘图标初始化完成");
            
            // 显示启动通知
            ToastHelper.ShowToast(_trayIcon, "WallpaperSync 已启动", "Windows虚拟桌面壁纸同步工具正在运行", ToastIconType.Info);
        }

        private void OnTrayIconClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Logger.LogDebug("[TrayController] 托盘图标右键点击");
                var customMenu = _menuBuilder.CreateCustomTrayMenu();
                customMenu.ShowMenu(Control.MousePosition);
            }
        }

        private void OnTrayIconDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Logger.LogDebug("[TrayController] 托盘图标双击");
                ShowApplicationInfo();
            }
        }

        private void ShowApplicationInfo()
        {
            var appInfo = "WallpaperSync v1.0\n\n" +
                         "Windows虚拟桌面壁纸同步工具\n" +
                         "自动检测壁纸变更并同步到所有虚拟桌面\n\n" +
                         "右键点击托盘图标查看更多选项";
            
            MessageBox.Show(appInfo, "关于 WallpaperSync", 
                          MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        
        private void OnWallpaperSyncCompleted(string wallpaperPath, bool success)
        {
            if (success)
            {
                var fileName = System.IO.Path.GetFileName(wallpaperPath);
                ToastHelper.ShowToast(_trayIcon, "壁纸已同步", $"已成功同步到所有虚拟桌面: {fileName}", ToastIconType.Success);
            }
            else
            {
                ToastHelper.ShowToast(_trayIcon, "同步失败", "壁纸同步失败，请查看日志", ToastIconType.Error);
            }
        }
        
        private void ShowNotificationCallback(string title, string text, ToolTipIcon icon)
        {
            // 转换ToolTipIcon到ToastIconType
            var toastIconType = icon switch
            {
                ToolTipIcon.Warning => ToastIconType.Warning,
                ToolTipIcon.Error => ToastIconType.Error,
                ToolTipIcon.Info => ToastIconType.Info,
                _ => ToastIconType.Info
            };
            
            ToastHelper.ShowToast(_trayIcon, title, text, toastIconType);
        }

        public void UpdateTrayIcon(string newText = null)
        {
            if (!_disposed && _trayIcon != null)
            {
                if (!string.IsNullOrEmpty(newText))
                {
                    _trayIcon.Text = newText;
                }
            }
        }


        private Icon LoadCustomIcon()
        {
            try
            {
                // 尝试从应用程序目录加载自定义图标
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                _iconPath = System.IO.Path.Combine(appDir, "icons", "icon.ico");
                
                if (System.IO.File.Exists(_iconPath))
                {
                    // 使用SystemInformation.SmallIconSize强制选择合适的分辨率
                    var iconSize = SystemInformation.SmallIconSize;
                    var icon = new Icon(_iconPath, iconSize);
                    
                    return icon;
                }
                
                // 尝试从相对路径加载
                var relativePath = System.IO.Path.Combine("..", "icons", "icon.ico");
                if (System.IO.File.Exists(relativePath))
                {
                    _iconPath = relativePath;
                    var iconSize = SystemInformation.SmallIconSize;
                    var icon = new Icon(relativePath, iconSize);
                    
                    return icon;
                }
                
                Logger.Log("[TrayController] 自定义图标文件未找到，使用系统默认图标");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log($"[TrayController] 加载自定义图标失败: {ex.Message}");
                return null;
            }
        }
        
        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            try
            {
                Logger.Log("[TrayController] 检测到显示设置变更，刷新托盘图标");
                
                // 延迟刷新，确保系统设置已完全更新
                System.Threading.Timer refreshTimer = null;
                refreshTimer = new System.Threading.Timer(_ =>
                {
                    try
                    {
                        if (!_disposed && _trayIcon != null && !string.IsNullOrEmpty(_iconPath))
                        {
                            var newIcon = LoadCustomIcon() ?? SystemIcons.Application;
                            
                            // 在UI线程上更新图标
                            if (_trayIcon.Icon != null)
                            {
                                _trayIcon.Icon.Dispose();
                            }
                            _trayIcon.Icon = newIcon;
                            
                            Logger.Log("[TrayController] 托盘图标已刷新");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[TrayController] 刷新托盘图标失败: {ex.Message}");
                    }
                    finally
                    {
                        refreshTimer?.Dispose();
                    }
                }, null, 1000, System.Threading.Timeout.Infinite); // 延迟1秒执行
            }
            catch (Exception ex)
            {
                Logger.Log($"[TrayController] 处理显示设置变更失败: {ex.Message}");
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                Logger.Log("[TrayController] 开始释放资源");
                
                // 取消显示设置变更监听
                try
                {
                    SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[TrayController] 取消显示设置监听失败: {ex.Message}");
                }
                
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }

                Logger.Log("[TrayController] 资源释放完成");
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}