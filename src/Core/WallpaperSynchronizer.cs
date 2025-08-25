using System;
using System.Runtime.InteropServices;
using WallpaperSync.Core;
using Microsoft.Win32;

namespace WallpaperSync
{
    /// <summary>
    /// 壁纸同步模块 - 使用IDesktopWallpaper COM接口
    /// 负责执行具体的壁纸同步逻辑，支持Windows 11虚拟桌面
    /// </summary>
    public class WallpaperSynchronizer : IDisposable
    {
        // IDesktopWallpaper COM接口定义
        [ComImport]
        [Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, 
                             [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetMonitorDevicePathAt(uint monitorIndex);
            
            uint GetMonitorDevicePathCount();
            
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out RECT displayRect);
            
            void SetBackgroundColor(uint color);
            
            uint GetBackgroundColor();
            
            void SetPosition(DESKTOP_WALLPAPER_POSITION position);
            
            DESKTOP_WALLPAPER_POSITION GetPosition();
            
            void SetSlideshow(IntPtr items);
            
            IntPtr GetSlideshow();
            
            void SetSlideshowOptions(DESKTOP_SLIDESHOW_OPTIONS options, uint slideshowTick);
            
            void GetSlideshowOptions(out DESKTOP_SLIDESHOW_OPTIONS options, out uint slideshowTick);
            
            void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, DESKTOP_SLIDESHOW_DIRECTION direction);
            
            DESKTOP_SLIDESHOW_STATE GetStatus();
            
            void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
        }
        
        // COM接口相关枚举和结构
        private enum DESKTOP_WALLPAPER_POSITION
        {
            DWPOS_CENTER = 0,
            DWPOS_TILE = 1,
            DWPOS_STRETCH = 2,
            DWPOS_FIT = 3,
            DWPOS_FILL = 4,
            DWPOS_SPAN = 5
        }
        
        private enum DESKTOP_SLIDESHOW_OPTIONS
        {
            DSO_SHUFFLEIMAGES = 0x01
        }
        
        private enum DESKTOP_SLIDESHOW_STATE
        {
            DSS_ENABLED = 0x01,
            DSS_SLIDESHOW = 0x02,
            DSS_DISABLED_BY_REMOTE_SESSION = 0x04
        }
        
        private enum DESKTOP_SLIDESHOW_DIRECTION
        {
            DSD_FORWARD = 0,
            DSD_BACKWARD = 1
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        
        // COM对象实例
        private IDesktopWallpaper desktopWallpaper;
        private bool disposed = false;
        
        /// <summary>
        /// 初始化壁纸同步器
        /// </summary>
        public WallpaperSynchronizer()
        {
            try
            {
                Logger.Log("[WallpaperSynchronizer] 初始化 IDesktopWallpaper COM接口...");
                
                // 创建COM对象
                Type type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"));
                object comObject = Activator.CreateInstance(type);
                desktopWallpaper = (IDesktopWallpaper)comObject;
                
                Logger.Log("[WallpaperSynchronizer] IDesktopWallpaper COM接口初始化成功");
            }
            catch (Exception ex)
            {
                Logger.Log("[WallpaperSynchronizer] COM接口初始化失败: " + ex.Message);
                throw;
            }
        }
        
        /// <summary>
        /// 同步壁纸到所有虚拟桌面
        /// </summary>
        public void SyncWallpaper()
        {
            // 调用重载方法，使用当前注册表中的壁纸路径
            SyncWallpaper(null);
        }

        /// <summary>
        /// 同步指定壁纸到所有虚拟桌面 - 🚨 紧急修复：支持指定壁纸路径
        /// </summary>
        /// <param name="wallpaperPath">指定的壁纸路径，如果为null则从注册表获取</param>
        public void SyncWallpaper(string wallpaperPath)
        {
            if (disposed)
            {
                Logger.Log("[WallpaperSynchronizer] 对象已释放，无法执行同步");
                return;
            }
            
            try
            {
                Logger.Log("[WallpaperSynchronizer] ========== 开始壁纸同步 (IDesktopWallpaper) ==========");
                
                // 🚨 紧急修复：优先使用传入的壁纸路径，如果没有则从注册表获取
                string currentWallpaper = string.IsNullOrEmpty(wallpaperPath) ? 
                    GetCurrentWallpaperPath() : wallpaperPath;
                
                if (!string.IsNullOrEmpty(wallpaperPath))
                {
                    Logger.Log("[WallpaperSynchronizer] 使用指定壁纸路径: " + wallpaperPath);
                }
                else
                {
                    Logger.Log("[WallpaperSynchronizer] 从注册表获取壁纸路径");
                }
                
                if (string.IsNullOrEmpty(currentWallpaper))
                {
                    Logger.Log("[WallpaperSynchronizer] 无法获取当前壁纸路径，同步终止");
                    return;
                }
                
                Logger.Log("[WallpaperSynchronizer] 当前壁纸: " + currentWallpaper);
                
                // 验证文件是否存在
                if (!System.IO.File.Exists(currentWallpaper))
                {
                    Logger.Log("[WallpaperSynchronizer] 壁纸文件不存在: " + currentWallpaper);
                    return;
                }
                
                // 使用IDesktopWallpaper接口设置壁纸到所有监视器
                SetWallpaperToAllMonitors(currentWallpaper);
                
                Logger.Log("[WallpaperSynchronizer] ========== 壁纸同步完成 (IDesktopWallpaper) ==========");
            }
            catch (Exception ex)
            {
                Logger.Log("[WallpaperSynchronizer] 壁纸同步过程出错: " + ex.Message);
                Logger.Log("[WallpaperSynchronizer] 错误堆栈: " + ex.StackTrace);
            }
        }
        
        /// <summary>
        /// 获取当前壁纸路径
        /// </summary>
        /// <returns>壁纸文件路径</returns>
        public string GetCurrentWallpaperPath()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (key != null)
                    {
                        object value = key.GetValue("Wallpaper");
                        string wallpaperPath = value != null ? value.ToString() : "";
                        Logger.Log("[WallpaperSynchronizer] 获取当前壁纸路径: " + wallpaperPath);
                        return wallpaperPath;
                    }
                    else
                    {
                        Logger.Log("[WallpaperSynchronizer] 无法打开注册表键: Control Panel\\Desktop");
                        return "";
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("[WallpaperSynchronizer] 读取壁纸路径失败: " + ex.Message);
                return "";
            }
        }
        
        /// <summary>
        /// 设置壁纸到所有监视器
        /// </summary>
        /// <param name="wallpaperPath">壁纸文件路径</param>
        private void SetWallpaperToAllMonitors(string wallpaperPath)
        {
            try
            {
                if (string.IsNullOrEmpty(wallpaperPath))
                {
                    Logger.Log("[WallpaperSynchronizer] 壁纸路径为空，跳过设置");
                    return;
                }
                
                Logger.Log("[WallpaperSynchronizer] 开始使用IDesktopWallpaper设置壁纸: " + wallpaperPath);
                
                // 获取监视器数量
                uint monitorCount = desktopWallpaper.GetMonitorDevicePathCount();
                Logger.Log("[WallpaperSynchronizer] 检测到监视器数量: " + monitorCount);
                
                if (monitorCount == 0)
                {
                    Logger.Log("[WallpaperSynchronizer] 未检测到任何监视器，使用null设置所有监视器");
                    // 如果没有检测到监视器，使用null参数设置所有监视器
                    desktopWallpaper.SetWallpaper(null, wallpaperPath);
                    Logger.Log("[WallpaperSynchronizer] 壁纸设置成功 (所有监视器)");
                }
                else
                {
                    // 设置壁纸到每个监视器
                    for (uint i = 0; i < monitorCount; i++)
                    {
                        try
                        {
                            string monitorPath = desktopWallpaper.GetMonitorDevicePathAt(i);
                            Logger.Log("[WallpaperSynchronizer] 设置监视器 " + i + " (" + monitorPath + ")");
                            
                            desktopWallpaper.SetWallpaper(monitorPath, wallpaperPath);
                            Logger.Log("[WallpaperSynchronizer] 监视器 " + i + " 壁纸设置成功");
                        }
                        catch (Exception ex)
                        {
                            Logger.Log("[WallpaperSynchronizer] 监视器 " + i + " 设置失败: " + ex.Message);
                        }
                    }
                    
                    // 额外设置一次所有监视器，确保虚拟桌面同步
                    Logger.Log("[WallpaperSynchronizer] 执行全局壁纸设置，确保虚拟桌面同步");
                    desktopWallpaper.SetWallpaper(null, wallpaperPath);
                    Logger.Log("[WallpaperSynchronizer] 全局壁纸设置完成");
                }
            }
            catch (COMException comEx)
            {
                Logger.Log("[WallpaperSynchronizer] COM异常: HRESULT=0x" + comEx.HResult.ToString("X") + ", 消息: " + comEx.Message);
            }
            catch (Exception ex)
            {
                Logger.Log("[WallpaperSynchronizer] 设置壁纸时出错: " + ex.Message);
            }
        }
        
        /// <summary>
        /// 释放COM对象资源
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
                    // 释放托管资源
                }
                
                // 释放COM对象
                if (desktopWallpaper != null)
                {
                    try
                    {
                        Logger.Log("[WallpaperSynchronizer] 释放IDesktopWallpaper COM对象");
                        Marshal.ReleaseComObject(desktopWallpaper);
                        desktopWallpaper = null;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("[WallpaperSynchronizer] 释放COM对象时出错: " + ex.Message);
                    }
                }
                
                disposed = true;
            }
        }
        
        /// <summary>
        /// 析构函数，确保COM对象被释放
        /// </summary>
        ~WallpaperSynchronizer()
        {
            Dispose(false);
        }
    }
}