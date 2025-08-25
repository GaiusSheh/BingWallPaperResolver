using System;
using System.IO;
using System.Windows.Forms;
using WallpaperSync.Core;
using Microsoft.Toolkit.Uwp.Notifications;

namespace WallpaperSync.UI
{
    public static class ToastHelper
    {
        private const string AppName = "WallpaperSync";
        
        public static void ShowToast(NotifyIcon notifyIcon, string title, string content, ToastIconType iconType = ToastIconType.Info)
        {
            try
            {
                // 获取图标路径
                var iconPath = GetAppIconPath(notifyIcon);
                
                // 创建现代Toast通知
                var toastBuilder = new ToastContentBuilder()
                    .AddText(title)
                    .AddText(content);
                
                // 如果有自定义图标，添加到Toast
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    toastBuilder.AddAppLogoOverride(new Uri($"file:///{iconPath.Replace('\\', '/')}"));
                }
                
                toastBuilder.Show();
                
                Logger.LogDebug($"[ToastHelper] 现代Toast通知已显示: {title}");
            }
            catch (Exception ex)
            {
                Logger.Log($"[ToastHelper] Toast通知失败，使用Balloon Tip fallback: {ex.Message}");
                // Fallback to balloon tip
                ShowFallbackNotification(notifyIcon, title, content, iconType);
            }
        }
        
        /// <summary>
        /// 获取应用图标路径
        /// </summary>
        private static string GetAppIconPath(NotifyIcon notifyIcon)
        {
            try
            {
                // 尝试从应用程序目录获取图标
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var iconPath = Path.Combine(appDir, "icons", "icon.ico");
                
                if (File.Exists(iconPath))
                {
                    return iconPath;
                }
                
                // 尝试从相对路径获取
                var relativePath = Path.Combine("..", "icons", "icon.ico");
                if (File.Exists(relativePath))
                {
                    return Path.GetFullPath(relativePath);
                }
                
                Logger.LogDebug("[ToastHelper] 未找到自定义图标，将使用系统默认");
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogDebug($"[ToastHelper] 获取图标路径失败: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 使用指定的NotifyIcon显示Balloon Tip通知 (Fallback)
        /// </summary>
        private static void ShowFallbackNotification(NotifyIcon notifyIcon, string title, string content, ToastIconType iconType)
        {
            try
            {
                if (notifyIcon == null)
                {
                    Logger.Log("[ToastHelper] NotifyIcon为空，无法显示通知");
                    return;
                }
                
                var toolTipIcon = ConvertToToolTipIcon(iconType);
                
                // 确保图标可见
                notifyIcon.Visible = true;
                notifyIcon.ShowBalloonTip(3000, title, content, toolTipIcon);
                
                Logger.LogDebug($"[ToastHelper] Fallback通知已显示: {title}");
            }
            catch (Exception fallbackEx)
            {
                Logger.Log($"[ToastHelper] Fallback通知也失败: {fallbackEx.Message}");
            }
        }
        
        /// <summary>
        /// 转换ToastIconType到ToolTipIcon
        /// </summary>
        private static ToolTipIcon ConvertToToolTipIcon(ToastIconType iconType)
        {
            return iconType switch
            {
                ToastIconType.Success => ToolTipIcon.Info,
                ToastIconType.Warning => ToolTipIcon.Warning,
                ToastIconType.Error => ToolTipIcon.Error,
                _ => ToolTipIcon.Info
            };
        }
        
        
        /// <summary>
        /// 测试Toast通知是否可用（当前版本始终返回false，使用传统通知）
        /// </summary>
        public static bool IsToastAvailable()
        {
            return false; // 当前版本使用传统通知
        }
    }
    
    public enum ToastIconType
    {
        Info,
        Success,
        Warning,
        Error
    }
}