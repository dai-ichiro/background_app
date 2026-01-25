using System;
using System.IO.Ports;
using System.Windows.Forms;

namespace background_app
{
    public partial class Form1 : Form
    {
        private SerialPort _serialPort;

        public Form1()
        {
            InitializeComponent();

            // 1. プロパティでも設定可能ですが、念のためコードでも指定
            this.ShowInTaskbar = false;      // タスクバーに表示しない
            this.WindowState = FormWindowState.Minimized; // 最小化状態で起動
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
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
                    SetupSerialPort();
                }
            }
            base.SetVisibleCore(value);
        }

        private void SetupSerialPort()
        {
            try
            {
                _serialPort = new SerialPort();
                _serialPort.PortName = "COM3";
                _serialPort.BaudRate = 9600;
                // Python側 (main.py) の設定に合わせて 8N1 に設定
                _serialPort.Parity = Parity.None;
                _serialPort.DataBits = 8;
                _serialPort.StopBits = StopBits.One;

                // イベントハンドラの登録
                _serialPort.DataReceived += new SerialDataReceivedEventHandler(DataReceivedHandler);

                // シリアルポートを開く
                _serialPort.Open();
            }
            catch (Exception ex)
            {
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
                    string commandName = GetCommandName(data);

                    // UI スレッドセーフに MessageBox を表示
                    this.Invoke(new Action(() =>
                    {
                        MessageBox.Show($"コマンド: {data}\n{commandName}", "受信データ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
            }
            catch (Exception ex)
            {
                this.Invoke(new Action(() =>
                {
                    MessageBox.Show($"データ受信エラー: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }));
            }
        }

        private string GetCommandName(int command)
        {
            switch (command)
            {
                case 20:
                    return "10秒戻る";
                case 21:
                    return "10秒進む";
                case 22:
                    return "再生/停止";
                case 23:
                    return "フルスクリーン";
                default:
                    return "不明なコマンド";
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
    }
}
