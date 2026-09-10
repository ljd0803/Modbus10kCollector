using Modbus10kCollector.Services;
using Modbus10kCollector.Views;
using NLog;
using Prism.DryIoc;
using Prism.Ioc;
using System.Windows;

namespace Modbus10kCollector
{
    public partial class App : PrismApplication
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. WPF UI调度器异常（UI线程）
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            // 2. 普通后台线程未处理异常
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // 3. Task异步任务未观测异常（await漏掉try-catch的后台Task）
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        #region 全局异常事件
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // UI线程异常
            var ex = e.Exception;
            _logger.Fatal(ex, $"【UI线程全局异常】{ex.Message}");

            // 设置Handled=true：阻止WPF直接崩溃退出；false则程序崩溃
            e.Handled = true;

            // 可选弹窗提示用户（UI线程可以弹窗）
            MessageBox.Show($"UI发生异常：\r\n{ex.Message}\r\n详情已写入日志", "UI异常", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            // 后台原生线程异常，这里无法阻止进程终止（CLR行为）
            if (e.ExceptionObject is Exception ex)
            {
                _logger.Fatal(ex, $"【后台线程致命异常】IsTerminating:{e.IsTerminating}");
            }
            else
            {
                _logger.Fatal($"【后台非托管异常对象】{e.ExceptionObject}");
            }
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            // 异步Task异常，await没捕获、被GC回收时触发
            var ex = e.Exception;
            _logger.Error(ex, $"【后台Task未捕获异常】{ex.Message}");

            // 标记为已观察，防止进程崩溃
            e.SetObserved();
        }
        #endregion
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
