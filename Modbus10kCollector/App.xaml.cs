using Prism.DryIoc;
using Prism.Ioc;
using Modbus10kCollector.Services;
using Modbus10kCollector.Views;
using System.Windows;

namespace Modbus10kCollector
{
    public partial class App : PrismApplication
    {
        protected override Window CreateShell()
        {
            return Container.Resolve<MainWindow>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // 注册采集服务为单例
            containerRegistry.RegisterSingleton<PlcCollectService>();
            containerRegistry.RegisterSingleton<MqttPublishService>();
            containerRegistry.RegisterSingleton<DeviceConfigService>(); 
            containerRegistry.RegisterSingleton<MqttConfigService>(); 
        }
    }
}
