using NLog;
using NModbus;
using Prism.Mvvm;
using Stateless;
using System;
using System.Threading;

namespace Modbus10kCollector.Models
{
    /// <summary>
    /// PLC设备状态枚举，Stateless状态机
    /// </summary>
    public enum PlcState
    {
        /// <summary>空闲，连接正常，等待下一轮采集</summary>
        Idle,
        /// <summary>正在建立TCP连接</summary>
        Connecting,
        /// <summary>正在采集寄存器（Modbus读写中）</summary>
        Collecting,
        /// <summary>连接已断开（TCP通道不存在，需要重连）</summary>
        Disconnected,
        /// <summary>故障：连接存在但是读写持续失败（通讯链路存在但PLC无响应）</summary>
        Fault
    }
    public enum PlcEvent
    {
        /// <summary>发起连接</summary>
        Connect,
        /// <summary>连接成功</summary>
        ConnectSuccess,
        /// <summary>连接失败</summary>
        ConnectFail,

        /// <summary>启动一轮采集</summary>
        StartCollect,
        /// <summary>采集读取成功</summary>
        ReadSuccess,
        /// <summary>采集读取失败</summary>
        ReadFail,

        /// <summary>断开连接</summary>
        Disconnect,
        /// <summary>重置状态，从故障/断开恢复到空闲</summary>
        Reset
    }

    /// <summary>
    /// 单台PLC模型，内置Stateless状态机
    /// 每一台PLC一个实例，独占TCP连接+ModbusIpMaster
    /// </summary>
    public class PlcDevice : BindableBase
    {
        private static readonly ILogger _logger = LogManager.GetCurrentClassLogger();
        private readonly StateMachine<PlcState, PlcEvent> _stateMachine;

        #region UI绑定字段（带通知）
        private PlcState _currentState;
        public PlcState CurrentState
        {
            get => _currentState;
            private set => SetProperty(ref _currentState, value);
        }

        private int _continuousFailCount;
        public int ContinuousFailCount
        {
            get => _continuousFailCount;
            set => SetProperty(ref _continuousFailCount, value);
        }

        private DateTime _lastSuccessTime;
        public DateTime LastSuccessTime
        {
            get => _lastSuccessTime;
            set => SetProperty(ref _lastSuccessTime, value);
        }

        private ushort[] _lastReadValue = Array.Empty<ushort>();
        public ushort[] LastReadValue
        {
            get => _lastReadValue;
            set => SetProperty(ref _lastReadValue, value);
        }

        private DateTime _breakCoolEndTime;
        /// <summary>冷却结束时间，冷却期内禁止重连</summary>
        public DateTime BreakCoolEndTime
        {
            get => _breakCoolEndTime;
            set => SetProperty(ref _breakCoolEndTime, value);
        }
        #endregion

        #region Modbus资源 & 配置（从json读取）
        public IModbusMaster? Master { get; set; }
        public IModbusTransport? Transport { get; set; }

        public string DeviceId { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public int Port { get; set; }
        public byte SlaveId { get; set; }
        public ushort StartAddr { get; set; }
        public ushort ReadCount { get; set; }

        /// <summary>连续失败阈值，达到该次数自动断开</summary>
        public int BreakThreshold { get; set; } = 5;
        /// <summary>断开后冷却秒数，冷却期内不尝试重连</summary>
        public int BreakCoolSecond { get; set; } = 60;
        #endregion

        public PlcDevice()
        {
            // 初始状态：断开
            _stateMachine = new StateMachine<PlcState, PlcEvent>(PlcState.Disconnected);
            _currentState = PlcState.Disconnected;

            // 状态切换回调，更新UI绑定的CurrentState
            _stateMachine.OnTransitioned(t =>
            {
                _logger.Info($"PLC[{DeviceId}] 状态变更 {t.Source} → {t.Destination}，触发事件:{t.Trigger}");
                CurrentState = t.Destination;
            });

            // =====状态流转配置（修复Permit同状态报错版本）=====
            _stateMachine.Configure(PlcState.Disconnected)
                .Permit(PlcEvent.Connect, PlcState.Connecting)
                .Ignore(PlcEvent.Reset);

            _stateMachine.Configure(PlcState.Connecting)
                .Permit(PlcEvent.ConnectSuccess, PlcState.Idle)
                .Permit(PlcEvent.ConnectFail, PlcState.Disconnected)
                .Permit(PlcEvent.Disconnect, PlcState.Disconnected);

            _stateMachine.Configure(PlcState.Idle)
                .Permit(PlcEvent.StartCollect, PlcState.Collecting)
                .Permit(PlcEvent.Disconnect, PlcState.Disconnected)
                .Permit(PlcEvent.Reset, PlcState.Disconnected);

            _stateMachine.Configure(PlcState.Collecting)
                .Permit(PlcEvent.ReadSuccess, PlcState.Idle)
                .Permit(PlcEvent.ReadFail, PlcState.Fault)
                .Permit(PlcEvent.Disconnect, PlcState.Disconnected)
                .Permit(PlcEvent.Reset, PlcState.Disconnected);

            _stateMachine.Configure(PlcState.Fault)
                .Permit(PlcEvent.StartCollect, PlcState.Collecting)
                .Permit(PlcEvent.Disconnect, PlcState.Disconnected)
                .Permit(PlcEvent.Reset, PlcState.Disconnected);
        }

        public Task FireAsync(PlcEvent evt)
        {
            return _stateMachine.FireAsync(evt);
        }

        public bool CanFire(PlcEvent evt)
        {
            return _stateMachine.CanFire(evt);
        }

        /// <summary>释放Modbus TCP资源</summary>
        public void DisposeConnection()
        {
            if (Transport != null)
            {
                Transport.Dispose();
                Transport = null;
            }
            Master = null;
        }

        /// <summary>开启冷却计时</summary>
        public void StartBreakCool()
        {
            BreakCoolEndTime = DateTime.Now.AddSeconds(BreakCoolSecond);
        }

        /// <summary>判断是否处于冷却时间内</summary>
        public bool IsInBreakCool()
        {
            return DateTime.Now < BreakCoolEndTime;
        }
    }
}
