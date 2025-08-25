using System;
using Microsoft.Extensions.DependencyInjection;
using WallpaperSync.Core;
using WallpaperSync.Services;
using WallpaperSync.UI;

namespace WallpaperSync
{
    public static class ServiceContainer
    {
        public static IServiceProvider ConfigureServices()
        {
            Logger.Log("[ServiceContainer] 开始配置服务容器");

            var services = new ServiceCollection();

            // 注册Services层 - 统一生命周期管理
            services.AddSingleton<ConfigService>();
            services.AddSingleton<PerformanceService>();
            services.AddSingleton<StartupService>();
            services.AddSingleton<WallpaperService>();

            // 注册UI层
            services.AddSingleton<TrayController>();

            // 注册应用上下文
            services.AddSingleton<DemoContext>();

            // 构建服务提供者 - 启用验证模式
            var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
            
            // 验证关键服务依赖能正确解析
            ValidateServiceDependencies(serviceProvider);
            
            Logger.Log("[ServiceContainer] 服务容器配置完成，所有依赖验证通过");
            return serviceProvider;
        }

        /// <summary>
        /// 验证关键服务依赖，确保运行时不会出现依赖解析失败
        /// </summary>
        private static void ValidateServiceDependencies(IServiceProvider serviceProvider)
        {
            try
            {
                // 验证关键服务链路
                var configService = serviceProvider.GetRequiredService<ConfigService>();
                var performanceService = serviceProvider.GetRequiredService<PerformanceService>();
                var wallpaperService = serviceProvider.GetRequiredService<WallpaperService>();
                var trayController = serviceProvider.GetRequiredService<TrayController>();
                
                Logger.LogDebug("[ServiceContainer] 服务依赖验证通过，所有关键服务正常解析");
            }
            catch (Exception ex)
            {
                Logger.Log($"[ServiceContainer] ⚠️ 服务依赖验证失败: {ex.Message}");
                throw new InvalidOperationException($"服务容器配置错误，依赖验证失败: {ex.Message}", ex);
            }
        }
    }
}