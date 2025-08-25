using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using WallpaperSync.Core;

namespace WallpaperSync
{
    /// <summary>
    /// 应用程序主入口点 - 重构版本
    /// 使用依赖注入和分层架构
    /// </summary>
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                // 设置DPI感知模式 - 兼容.NET Framework 4.8环境
                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                Logger.Log("[Program] WallpaperSync 启动中...");
                
                // 配置服务容器
                var serviceProvider = ServiceContainer.ConfigureServices();
                
                // 创建应用上下文并运行
                using (var context = serviceProvider.GetRequiredService<DemoContext>())
                {
                    Logger.Log("[Program] 进入应用程序主循环");
                    Application.Run(context);
                }
                
                Logger.Log("[Program] WallpaperSync 正常退出");
            }
            catch (Exception ex)
            {
                Logger.Log($"[Program] 应用程序异常: {ex}");
                MessageBox.Show($"应用程序发生异常:\n{ex.Message}", "WallpaperSync 错误", 
                              MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}