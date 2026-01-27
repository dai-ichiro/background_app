using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;

namespace background_app
{
    public partial class Form1 : Form
    {
        private SerialPort _serialPort;
        private AppConfig _config;
        private string _logPath;

        public Form1()
        {
            InitializeComponent();
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
            // Logging state is unknown yet, so we don't log until config is loaded or if we default to true temporarily.
            // But user said "not necessarily needed", so default false.

            // 1. プロパティでも設定可能ですが、念のためコードでも指定
            this.ShowInTaskbar = false;      // タスクバーに表示しない
            this.WindowState = FormWindowState.Minimized; // 最小化状態で起動
        }

        private void Log(string message)
        {
            try
            {
                if (_config != null && _config.Settings != null && _config.Settings.LoggingEnabled)
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    File.AppendAllText(_logPath, $"[{timestamp}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Logging failed, ignore
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            Log("Application Closing...");
            CloseSerialPort();
            base.OnFormClosing(e);
        }

        private bool _initialized = false;

        // 起動時にフォームを一瞬も表示させないための核心部分
        protected override void SetVisibleCore(bool value)
        {
            if (!this.IsHandleCreated)
            {
                value = false;
                CreateHandle();

                // ハンドル作成直後に初期化を行う
                if (!_initialized)
                {
                    _initialized = true;
                    // Load config first to determine settings (logging, com port)
                    LoadKeyConfig();
                    SetupSerialPort();
                }
            }
            base.SetVisibleCore(value);
        }

        private void SetupSerialPort()
        {
            try
            {
                Log("Starting SetupSerialPort...");
                string portNumber = "";

                // Prioritize Config
                if (_config != null && _config.Settings != null && _config.Settings.ComPort > 0)
                {
                    portNumber = _config.Settings.ComPort.ToString();
                    Log($"Using COM port from config: {portNumber}");
                }

                if (string.IsNullOrEmpty(portNumber))
                {
                    Log("COM port not specified or invalid.");
                    MessageBox.Show("COM portを指定して下さい", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Application.Exit();
                    return;
                }

                Log($"Selected COM Port: COM{portNumber}");

                _serialPort = new SerialPort();
                _serialPort.PortName = "COM" + portNumber;
                _serialPort.BaudRate = 9600;
                // Python側 (main.py) の設定に合わせて 8N1 に設定
                _serialPort.Parity = Parity.None;
                _serialPort.DataBits = 8;
                _serialPort.StopBits = StopBits.One;

                // イベントハンドラの登録
                _serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);

                // シリアルポートを開く
                _serialPort.Open();
                Log("Serial port opened successfully.");
            }
            catch (Exception ex)
            {
                Log($"SetupSerialPort Error: {ex}");
                string errorMessage = $"シリアルポートの初期化に失敗しました: {ex.Message}";
                MessageBox.Show(errorMessage, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DataReceivedHandler(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                SerialPort sp = (SerialPort)sender;

                // 受信可能なバイト数分ループして処理
                while (sp.BytesToRead > 0)
                {
                    int data = sp.ReadByte();
                    string dataStr = data.ToString();
                    Log($"Received Byte: {data} (String: {dataStr})");

                    if (_config != null && _config.Actions != null && _config.Actions.ContainsKey(dataStr))
                    {
                        Log($"Found config for key: {dataStr}");
                        foreach (var step in _config.Actions[dataStr])
                        {
                            if (step.Type == "key")
                            {
                                Log($"Executing Key: {step.Value}");
                                int holdTime = 0;
                                if (_config.Settings != null)
                                {
                                    holdTime = _config.Settings.KeyHoldTime;
                                }
                                this.Invoke(new Action(() => InputSender.Send(step.Value, holdTime)));
                            }
                            else if (step.Type == "wait")
                            {
                                int ms;
                                if (int.TryParse(step.Value, out ms))
                                {
                                    Log($"Waiting: {ms}ms");
                                    Thread.Sleep(ms);
                                }
                                else
                                {
                                    Log($"Invalid wait value: {step.Value}");
                                }
                            }
                        }
                    }
                    else
                    {
                        Log($"No config found for key: {dataStr}. Doing nothing.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"DataReceivedHandler Error: {ex}");
                this.Invoke(new Action(() =>
                {
                    MessageBox.Show($"データ受信エラー: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
        }

        // コンテキストメニュー：終了（ここがご要望のポイントです）
        private void 終了ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // シリアルポートをクローズ
            CloseSerialPort();

            // タスクトレイのアイコンを明示的に非表示にする（これをしないと終了後もアイコンが残ることがあります）
            notifyIcon1.Visible = false;

            // プロセスを完全に終了させる
            Application.Exit();
        }

        private void CloseSerialPort()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                    _serialPort.Dispose();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"シリアルポートのクローズエラー: {ex.Message}");
            }
        }

        private void LoadKeyConfig()
        {
            try
            {
                string exePath = AppDomain.CurrentDomain.BaseDirectory;
                string configPath = Path.Combine(exePath, "key_config.json");
                // Cannot log here yet as config is not loaded.

                if (File.Exists(configPath))
                {
                    using (FileStream fs = new FileStream(configPath, FileMode.Open))
                    {
                        var settings = new DataContractJsonSerializerSettings();
                        settings.UseSimpleDictionaryFormat = true;
                        var serializer = new DataContractJsonSerializer(typeof(AppConfig), settings);
                        _config = (AppConfig)serializer.ReadObject(fs);
                    }
                }

                // Now config is loaded, log if enabled
                Log($"Config loaded from: {configPath}");
                if (_config != null && _config.Actions != null)
                {
                    Log($"Loaded {_config.Actions.Count} actions. Keys: {string.Join(", ", _config.Actions.Keys)}");
                }
                else
                {
                    Log("Config loaded but Actions list is empty or null.");
                }
            }
            catch (Exception ex)
            {
                // Force log if error? No, if logging is disabled, don't log.
                // But this is an error, maybe user wants to know.
                // For now, respect the flag (which is null/false).
                MessageBox.Show($"設定ファイルの読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    [DataContract]
    public class AppConfig
    {
        [DataMember(Name = "settings")]
        public AppSettings Settings { get; set; }

        [DataMember(Name = "actions")]
        public Dictionary<string, List<ActionStep>> Actions { get; set; }
    }

    [DataContract]
    public class AppSettings
    {
        [DataMember(Name = "com_port")]
        public int ComPort { get; set; }

        [DataMember(Name = "logging_enabled")]
        public bool LoggingEnabled { get; set; }

        [DataMember(Name = "key_hold_time")]
        public int KeyHoldTime { get; set; }
    }

    [DataContract]
    public class ActionStep
    {
        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "value")]
        public string Value { get; set; }
    }
}
