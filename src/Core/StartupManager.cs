using System;
using System.Reflection;
using Microsoft.Win32;
using WallpaperSync.Core;

namespace WallpaperSync
{
    public class StartupManager
    {
        private const string REGISTRY_RUN_PATH = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string APPLICATION_NAME = "WallpaperSync";
        
        public bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REGISTRY_RUN_PATH))
                {
                    if (key != null)
                    {
                        string registryValue = key.GetValue(APPLICATION_NAME) as string;
                        string currentPath = GetExecutablePath();
                        
                        bool isEnabled = !string.IsNullOrEmpty(registryValue) && 
                                       registryValue.Equals(currentPath, StringComparison.OrdinalIgnoreCase);
                        
                        Logger.Log("[StartupManager] 检测自启状态: " + (isEnabled ? "已启用" : "未启用"));
                        Logger.Log("[StartupManager] 注册表路径: " + (registryValue ?? "null"));
                        Logger.Log("[StartupManager] 当前程序路径: " + currentPath);
                        
                        return isEnabled;
                    }
                    else
                    {
                        Logger.Log("[StartupManager] 无法打开注册表运行键");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("[StartupManager] 检测自启状态失败: " + ex.Message);
                return false;
            }
        }
        
        public bool EnableStartup()
        {
            try
            {
                string executablePath = GetExecutablePath();
                
                if (string.IsNullOrEmpty(executablePath))
                {
                    Logger.Log("[StartupManager] 无法获取程序路径，启用自启失败");
                    return false;
                }
                
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REGISTRY_RUN_PATH, true))
                {
                    if (key != null)
                    {
                        key.SetValue(APPLICATION_NAME, executablePath);
                        Logger.Log("[StartupManager] 开机自启启用成功: " + executablePath);
                        
                        bool verifyResult = IsStartupEnabled();
                        if (verifyResult)
                        {
                            Logger.Log("[StartupManager] 自启设置验证成功");
                            return true;
                        }
                        else
                        {
                            Logger.Log("[StartupManager] 自启设置验证失败");
                            return false;
                        }
                    }
                    else
                    {
                        Logger.Log("[StartupManager] 无法以写入模式打开注册表运行键");
                        return false;
                    }
                }
            }
            catch (System.Security.SecurityException secEx)
            {
                Logger.Log("[StartupManager] 启用开机自启权限不足: " + secEx.Message);
                return false;
            }
            catch (UnauthorizedAccessException authEx)
            {
                Logger.Log("[StartupManager] 启用开机自启访问被拒绝: " + authEx.Message);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log("[StartupManager] 启用开机自启失败: " + ex.Message);
                return false;
            }
        }
        
        public bool DisableStartup()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REGISTRY_RUN_PATH, true))
                {
                    if (key != null)
                    {
                        object existingValue = key.GetValue(APPLICATION_NAME);
                        
                        if (existingValue != null)
                        {
                            key.DeleteValue(APPLICATION_NAME, false);
                            Logger.Log("[StartupManager] 开机自启禁用成功，已删除注册表项");
                        }
                        else
                        {
                            Logger.Log("[StartupManager] 注册表中无自启项，无需禁用");
                        }
                        
                        bool verifyResult = IsStartupEnabled();
                        if (!verifyResult)
                        {
                            Logger.Log("[StartupManager] 自启禁用验证成功");
                            return true;
                        }
                        else
                        {
                            Logger.Log("[StartupManager] 自启禁用验证失败");
                            return false;
                        }
                    }
                    else
                    {
                        Logger.Log("[StartupManager] 无法以写入模式打开注册表运行键");
                        return false;
                    }
                }
            }
            catch (System.Security.SecurityException secEx)
            {
                Logger.Log("[StartupManager] 禁用开机自启权限不足: " + secEx.Message);
                return false;
            }
            catch (UnauthorizedAccessException authEx)
            {
                Logger.Log("[StartupManager] 禁用开机自启访问被拒绝: " + authEx.Message);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log("[StartupManager] 禁用开机自启失败: " + ex.Message);
                return false;
            }
        }
        
        public bool CanAccessStartupRegistry()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(REGISTRY_RUN_PATH, true))
                {
                    if (key != null)
                    {
                        Logger.Log("[StartupManager] 注册表访问权限检查: 正常");
                        return true;
                    }
                    else
                    {
                        Logger.Log("[StartupManager] 注册表访问权限检查: 无法打开键");
                        return false;
                    }
                }
            }
            catch (System.Security.SecurityException secEx)
            {
                Logger.Log("[StartupManager] 注册表访问权限检查: 权限不足 - " + secEx.Message);
                return false;
            }
            catch (UnauthorizedAccessException authEx)
            {
                Logger.Log("[StartupManager] 注册表访问权限检查: 访问被拒绝 - " + authEx.Message);
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log("[StartupManager] 注册表访问权限检查: 异常 - " + ex.Message);
                return false;
            }
        }
        
        private string GetExecutablePath()
        {
            try
            {
                string executablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                
                if (string.IsNullOrEmpty(executablePath))
                {
                    executablePath = Assembly.GetExecutingAssembly().Location;
                    if (executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        executablePath = executablePath.Replace(".dll", ".exe");
                    }
                }
                
                Logger.Log("[StartupManager] 获取程序路径: " + executablePath);
                return executablePath;
            }
            catch (Exception ex)
            {
                Logger.Log("[StartupManager] 获取程序路径失败: " + ex.Message);
                return "";
            }
        }
    }
}