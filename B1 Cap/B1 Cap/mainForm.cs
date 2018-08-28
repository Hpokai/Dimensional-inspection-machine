using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.IO;

namespace B1_Cap
{
    public partial class b1CapForm : Form
    {
        #region Global Variable
        private System.IO.Ports.SerialPort seriaPort;   // declair com port
        private PLCConn PLC;    // declair PLC 
        private Thread OperationThread;
        private bool isShutDown = false;

        #endregion

        #region Main Form
        public b1CapForm()
        {
            InitializeComponent();

            this.enableBinCheckBox = new CheckBox[7] {this.enableBin1CheckBox,this.enableBin2CheckBox,this.enableBin3CheckBox,this.enableBin4CheckBox,this.enableBin5CheckBox,this.enableBin6CheckBox,this.enableBin7CheckBox};
            this.minWidthBinTextBox = new TextBox[7] { this.minWidthBin1TextBox, this.minWidthBin2TextBox, this.minWidthBin3TextBox, this.minWidthBin4TextBox, this.minWidthBin5TextBox, this.minWidthBin6TextBox, this.minWidthBin7TextBox };
            this.maxWidthBinTextBox = new TextBox[7] { this.maxWidthBin1TextBox, this.maxWidthBin2TextBox, this.maxWidthBin3TextBox, this.maxWidthBin4TextBox, this.maxWidthBin5TextBox, this.maxWidthBin6TextBox, this.maxWidthBin7TextBox };
            this.minThinBinTextBox = new TextBox[7] { this.minThinBin1TextBox, this.minThinBin2TextBox, this.minThinBin3TextBox, this.minThinBin4TextBox, this.minThinBin5TextBox, this.minThinBin6TextBox, this.minThinBin7TextBox };
            this.maxThinBinTextBox = new TextBox[7] { this.maxThinBin1TextBox, this.maxThinBin2TextBox, this.maxThinBin3TextBox, this.maxThinBin4TextBox, this.maxThinBin5TextBox, this.maxThinBin6TextBox, this.maxThinBin7TextBox };
            this.setBinButton = new Button[7] { this.setBin1Button, this.setBin2Button, this.setBin3Button, this.setBin4Button, this.setBin5Button, this.setBin6Button, this.setBin7Button };

            this.totalNumTextBox.Text = "0";
            //// m3000 m3010 testing button visible  #testing
            //this.testM3000Button.Visible = true;
            //this.testM3010Button.Visible = true;
        }

        private void b1CapForm_Load(object sender, EventArgs e)
        {
            //-- for COM Port
            //foreach (string com in System.IO.Ports.SerialPort.GetPortNames())//取得所有可用的連接埠
            //{
            this.seriaPort = new System.IO.Ports.SerialPort("COM1");
            //    this.seriaPort = new System.IO.Ports.SerialPort(com);
            //    break;
            //}

            if (false == this.seriaPort.IsOpen)
            {
                // 9600、n、8、1、n
                //seriaPort.PortName = ;
                this.seriaPort.BaudRate = 9600;
                this.seriaPort.DataBits = 8;
                this.seriaPort.Parity = System.IO.Ports.Parity.None;
                this.seriaPort.StopBits = System.IO.Ports.StopBits.One;
                this.seriaPort.Encoding = Encoding.Default;
                try
                {
                    this.seriaPort.Open();
                }
                catch
                {
                    MessageBox.Show("請檢查 COM port，系統關閉！");
                    this.Close();
                    return;
                }
            }

            //-- for PLC
            this.PLC = new PLCConn("192.168.0.10", 2001);
            if (false == this.PLC.isConnect)
            {
                // if dry run, you must mark this section. #testing
                if (false == this.PLC.ConnectPLC())
                {
                    MessageBox.Show("請檢查 PLC 連線，系統關閉！");
                    this.Close();
                    return;
                }
            }

            this.InitializePlcData();

            //-- for Get measure data of bin
            this.GetBinMeasureData();
            for (int i = 0; i < 7; i++) 
            {
                this.minWidthBinTextBox[i].Text = this.bin_width[MIN, i].ToString("#0.000");
                this.maxWidthBinTextBox[i].Text = this.bin_width[MAX, i].ToString("#0.000");
                this.minThinBinTextBox[i].Text = this.bin_thin[MIN, i].ToString("#0.000");
                this.maxThinBinTextBox[i].Text = this.bin_thin[MAX, i].ToString("#0.000");

                this.enableBinCheckBox[i].Checked = this.bin_enable[i]; 
            }
            this.currentNgNumericUpDown.Value = this.bin_NG_num;

            //-- for Get Product info
            this.GetTotalNumAndDate();
            this.totalNumTextBox.Text = this.product_total_num.ToString();
            this.totalNumTimeLabel.Text = this.product_date;

            //-- for Thread Start
            this.OperationThread = new Thread(this.OperationThreadRun);
            this.OperationThread.Name = "Thread: Operation Run!";
            this.OperationThread.Priority = ThreadPriority.AboveNormal;
            this.OperationThread.IsBackground = true;
            this.OperationThread.Start();

            //-- for display timer
            this.displayTimer.Start();
        }

        private void b1CapForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.isShutDown = true;
            this.displayTimer.Stop();

            File.AppendAllText(@"doc\Data Log.txt", this.dataRichTextBox.Text + Environment.NewLine);
            
            this.SetTotalNumAndDate();

            System.Threading.Thread.Sleep(100);
            if (true == this.seriaPort.IsOpen) { this.seriaPort.Close(); }
            System.Threading.Thread.Sleep(100);
            if (null != this.PLC)
            {
                if (true == this.PLC.isConnect) { this.PLC.DisConnect(); }
            }
            System.Threading.Thread.Sleep(100);
            if (null != this.OperationThread) { this.OperationThread.Abort(); }
        }
        #endregion

        #region Other Subfunction
        private enum IO { LtoC_CaptureRun = 3000, CtoL_CaptureDone = 3008, LtoC_ResetRun = 3010, CtoL_ResetDone = 3018 };
        private String plcON = "1", plcOFF = "0";
        private string[,] plcState_Default = new string[2, 7] { { "0", "0", "0", "0", "0", "0", "0" }, { "0", "0", "0", "0", "0", "0", "0" } };
        private void InitializePlcData()
        {
            this.PLC.WriteM((int)IO.LtoC_CaptureRun, this.plcOFF);
            this.PLC.WriteM((int)IO.CtoL_CaptureDone, this.plcOFF);
            this.PLC.WriteM((int)IO.LtoC_ResetRun, this.plcOFF);
            this.PLC.WriteM((int)IO.CtoL_ResetDone, this.plcOFF);

            int Mcode = 3001;
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 7; j++) { PLC.WriteM(Mcode + j, this.plcState_Default[i, j]); }
                Mcode = 3011;
            }
        }

        private double min_width_1 = Properties.Settings.Default.minWidth1;
        private double min_width_2 = Properties.Settings.Default.minWidth2;
        private double min_thin_1 = Properties.Settings.Default.minThin1;
        private double min_thin_2 = Properties.Settings.Default.minThin2;
        private double min_thin_3 = Properties.Settings.Default.minThin3;
        private double max_width_1 = Properties.Settings.Default.maxWidth1;
        private double max_width_2 = Properties.Settings.Default.maxWidth2;
        private double max_thin_1 = Properties.Settings.Default.maxThin1;
        private double max_thin_2 = Properties.Settings.Default.maxThin2;
        private double max_thin_3 = Properties.Settings.Default.maxThin3;

        private const int MAX = 1, MIN = 0;
        private double[,] bin_width = new double[2, 7] { { 6.24, 6.24, 6.24, 6.26, 6.26, 6.26, 0 }, { 6.26, 6.26, 6.26, 6.28, 6.28, 6.28, 0 } };
        private double[,] bin_thin = new double[2, 7] { { 1.42, 1.44, 1.46, 1.42, 1.44, 1.46, 0 }, { 1.44, 1.46, 1.48, 1.44, 1.46, 1.48, 0 } };
        private bool[] bin_enable = new bool[7] { true, true, true, true, true, true, true };
        private int bin_NG_num = 7;

        private void GetBinMeasureData()
        {
            string directory_path = @"setting\";
            string file_name = "dimension.st";

            if (false == Directory.Exists(directory_path)) { Directory.CreateDirectory(directory_path); }

            string combination_path = Path.Combine(directory_path, file_name);
            if (true == File.Exists(combination_path)) 
            {
                string[] dimension =  System.IO.File.ReadAllLines(combination_path);
                int index = 0;
                double value = 0.0;
                for (int i = 0; i < 7; i++)
                {
                    if (true == double.TryParse(dimension[index++], out value)) { this.bin_width[MIN, i] = value; }
                    if (true == double.TryParse(dimension[index++], out value)) { this.bin_width[MAX, i] = value; }
                    if (true == double.TryParse(dimension[index++], out value)) { this.bin_thin[MIN, i] = value; }
                    if (true == double.TryParse(dimension[index++], out value)) { this.bin_thin[MAX, i] = value; }
                }
            }

            file_name = "enable.st";
            combination_path = Path.Combine(directory_path, file_name);
            if (true == File.Exists(combination_path))
            {
                string[] enable = System.IO.File.ReadAllLines(combination_path);
                bool value = false;
                for (int i = 0; i < 7; i++)
                { if (true == bool.TryParse(enable[i], out value)) { this.bin_enable[i] = value; } }

                int num = 0;
                if (true == int.TryParse(enable[7], out num)) { this.bin_NG_num = num; }
            }
        }
        private void SetBinMeasureData(bool isEnableOnly, bool isDimensionOnly)
        {
            string directory_path = @"setting\";
            string file_name = "dimension.st";

            if (false == Directory.Exists(directory_path)) { Directory.CreateDirectory(directory_path); }

            string combination_path = Path.Combine(directory_path, file_name);
            string[] dimension = new string[28];
            int index = 0;

            if (false == isEnableOnly)
            {
                for (int i = 0; i < 7; i++)
                {
                    dimension[index++] = this.bin_width[MIN, i].ToString();
                    dimension[index++] = this.bin_width[MAX, i].ToString();
                    dimension[index++] = this.bin_thin[MIN, i].ToString();
                    dimension[index++] = this.bin_thin[MAX, i].ToString();
                }
                System.IO.File.WriteAllLines(combination_path, dimension);
            }

            if (false == isDimensionOnly)
            {
                file_name = "enable.st";
                combination_path = Path.Combine(directory_path, file_name);
                string[] enable = new string[8];    // 7 + 1
                for (int i = 0; i < 7; i++) { enable[i] = this.bin_enable[i].ToString(); }
                enable[7] = this.bin_NG_num.ToString();
                System.IO.File.WriteAllLines(combination_path, enable);
            }
        }

        private void GetTotalNumAndDate()
        {
            string directory_path = @"setting\";
            string file_name = "product.st";

            if (false == Directory.Exists(directory_path)) { Directory.CreateDirectory(directory_path); }

            string combination_path = Path.Combine(directory_path, file_name);

            if (true == File.Exists(combination_path))
            {
                string[] product = System.IO.File.ReadAllLines(combination_path);
                Int64 value = 0;
                if (true == Int64.TryParse(product[0], out value)) { this.product_total_num = value; }
                this.product_date = product[1];
            }
        }
        private Int64 product_total_num = 0;
        private string product_date = "0000/00/00 00:00:00";
        private void SetTotalNumAndDate()
        {
            string directory_path = @"setting\";
            string file_name = "product.st";

            if (false == Directory.Exists(directory_path)) { Directory.CreateDirectory(directory_path); }

            string combination_path = Path.Combine(directory_path, file_name);

            string[] product = new string[2];
            product[0] = this.product_total_num.ToString();
            product[1] = this.product_date;
            System.IO.File.WriteAllLines(combination_path, product);           
        }
        #endregion

        #region Thread Start
        private void OperationThreadRun()
        {
            // Main Progress
            while (false == this.isShutDown)
            {
                this.RunInput();
                this.RunProcess();
                this.RunOutput();

                System.Threading.Thread.Sleep(20);
            }

            Console.WriteLine("THREAD STOP");
        }

        private enum MODE { MANUAL, AUTO };
        private MODE current_mode = MODE.MANUAL;
        private enum PROCESS { NONE, waitM3010ResetRun, setM3018ResetDone, waitM3000CaptureRun, CaptureRun, setM3008CaptureDone, Finish };
        private PROCESS current_process = PROCESS.NONE;

        private enum MODE_IO { LtoC_StartToManual = 3030, CtoL_ResponseManual = 3031, LtoC_StartToAuto = 3040, CtoL_ResponseAuto = 3041 };

        private int BufferSize = 1;

        private string in_M3030_Manual = string.Empty;
        private string in_M3040_Auto = string.Empty;

        private string in_M3000_CaptureRun = string.Empty;
        private string in_M3008_CaptureDone = string.Empty;
        private string in_M3010_ResetRun = string.Empty;
        private string in_M3018_ResetDone = string.Empty;

        private bool in_isGetManualSignal = false;
        private bool in_isGetAutoSignal = false;

        private bool in_isResetRun = false;
        private bool in_isCaptureRun = false;
        private bool in_isStartToGetMeasureData = false;

        private bool in_isCountCycleTime = false;
        private bool in_isShowCycleTime = false;

        private System.Diagnostics.Stopwatch stop_watch = new System.Diagnostics.Stopwatch();
        private string elapsed_time = string.Empty;

        private void RunInput()
        {
            this.in_M3030_Manual = this.PLC.ReadM((int)MODE_IO.LtoC_StartToManual, this.BufferSize);
            this.in_M3040_Auto = this.PLC.ReadM((int)MODE_IO.LtoC_StartToAuto, this.BufferSize);

            if (this.plcON == this.in_M3040_Auto)
            {
                this.in_isGetAutoSignal = true;
                this.current_mode = MODE.AUTO;
                this.current_process = PROCESS.NONE;
            }
            else if (this.plcON == this.in_M3030_Manual)
            {
                this.in_isGetManualSignal = true;
                this.current_mode = MODE.MANUAL;
            }

            if (MODE.AUTO == this.current_mode)
            {
                this.in_M3000_CaptureRun = this.PLC.ReadM((int)IO.LtoC_CaptureRun, this.BufferSize);
                this.in_M3008_CaptureDone = this.PLC.ReadM((int)IO.CtoL_CaptureDone, this.BufferSize);
                this.in_M3010_ResetRun = this.PLC.ReadM((int)IO.LtoC_ResetRun, this.BufferSize);
                this.in_M3018_ResetDone = this.PLC.ReadM((int)IO.CtoL_ResetDone, this.BufferSize);

                if (this.plcON == this.in_M3010_ResetRun)
                {
                    this.in_isResetRun = true;
                }
                if (this.plcON == this.in_M3000_CaptureRun)
                {
                    this.in_isCaptureRun = true;
                }

                // for cycle time 
                if (this.plcON == this.PLC.ReadM(3050, this.BufferSize))
                {
                    this.in_isCountCycleTime = true;

                    if (true == this.stop_watch.IsRunning)
                    {
                        this.stop_watch.Stop();     //碼錶停止
                        this.elapsed_time = (this.stop_watch.Elapsed.TotalMilliseconds / 1000.0).ToString("0.00000");
                        this.in_isShowCycleTime = true;
                    }

                    this.stop_watch.Reset();    //碼表歸零
                    this.stop_watch.Start();    //碼表開始計時
                }
            }
        }

        private bool out_isResetRun = false;
        private bool out_isResetDone = false;
        private bool out_isCaptureRun = false;
        private bool out_isStartToGetMeasureData = false;
        private bool out_isCaptureDone = false;
        private bool out_isFinish = false;
        private bool out_isGetManualSignal = false;
        private bool out_isGetAutoSignal = false;
        
        private uint[] out_binResult = new uint[2] {0,0};
        private double[] out_productWidth = new double[2]{0.0,0.0};
        private double[] out_productThin = new double[2]{0.0,0.0};

        private int sleep_count = 0;
        private int no_data_receive_count = 0;
        private void RunProcess()
        {
            switch (this.current_mode)
            {
                case MODE.MANUAL:
                    {
                        if (true == this.in_isGetManualSignal)
                        {
                            this.out_isGetManualSignal = this.in_isGetManualSignal;
                            this.in_isGetManualSignal = false;
                        }
                    }
                    break;
                case MODE.AUTO:
                    {
                        switch (this.current_process)
                        {
                            case PROCESS.NONE:
                                if (true == this.in_isGetAutoSignal)
                                {
                                    this.out_isGetAutoSignal = this.in_isGetAutoSignal;
                                    this.in_isGetAutoSignal = false;
                                }

                                this.in_isResetRun = false;
                                this.in_isCaptureRun = false;
                                this.in_isStartToGetMeasureData = false;

                                this.out_isResetRun = false;
                                this.out_isResetDone = false;
                                this.out_isCaptureRun = false;
                                this.out_isStartToGetMeasureData = false;
                                this.out_isCaptureDone = false;
                                this.out_isFinish = false;
                                break;
                            case PROCESS.waitM3010ResetRun:
                                if (true == this.in_isResetRun)
                                {
                                    // set 3001~3007 = 0 & 3010~3017 = 0
                                    this.out_isResetRun = this.in_isResetRun;
                                    this.in_isResetRun = false;
                                }
                                break;
                            case PROCESS.setM3018ResetDone:
                                this.out_isResetDone = true;
                                break;
                            case PROCESS.waitM3000CaptureRun:
                                if (true == this.in_isCaptureRun)
                                {
                                    this.out_isCaptureRun = this.in_isCaptureRun;
                                    this.in_isCaptureRun = false;
                                }
                                break;
                            case PROCESS.CaptureRun:
                                if (true == this.in_isStartToGetMeasureData)
                                {
                                    if (5 > this.no_data_receive_count)
                                    {
                                        this.out_isStartToGetMeasureData = this.CaptureKeyenceData(out this.out_binResult, out this.out_productWidth, out this.out_productThin);
                                    }
                                    else
                                    {
                                        this.out_isStartToGetMeasureData = true;
                                    }
                                    if (0 == this.out_binResult[0]) { this.out_binResult[0] = (uint)this.bin_NG_num; }
                                    if (0 == this.out_binResult[1]) { this.out_binResult[1] = (uint)this.bin_NG_num; }

                                    this.in_isStartToGetMeasureData = !this.out_isStartToGetMeasureData;

                                    //// random data #testing
                                    //Random rnd = new Random();
                                    //this.out_isStartToGetMeasureData = true;
                                    //this.in_isStartToGetMeasureData = false;
                                    //this.out_binResult[0] = (uint)rnd.Next(1,8);
                                    //this.out_binResult[1] = (uint)rnd.Next(1, 8); ;
                                    //this.out_productWidth[0] = 2.43;
                                    //this.out_productWidth[1] = 2.11;
                                    //this.out_productThin[0] = 1.24;
                                    //this.out_productThin[1] = 1.99;
                                }
                                break;
                            // if run too fast, i will add a 100ms sleep here;  PROCESS.sleepCaptureRun;
                            case PROCESS.setM3008CaptureDone:
                                //if (0 < this.sleep_count)
                                //{
                                this.out_isCaptureDone = true;
                                //}
                                //else
                                //{
                                //    this.sleep_count++;
                                //}
                                break;
                            case PROCESS.Finish:
                                //if (0 < this.sleep_count)
                                //{
                                this.out_isFinish = true;
                                //}
                                //else
                                //{
                                //    this.sleep_count++;
                                //}
                                break;
                            default:
                                break;
                        }
                    }
                    break;
                default:
                    break;
            }
        }


        private string fm_M3000_CaptureRun = string.Empty;
        private string fm_M3008_CaptureDone= string.Empty;
        private string fm_M3010_ResetRun =  string.Empty;
        private string fm_M3018_ResetDone = string.Empty;

        private uint[] fm_bin_resutl = new uint[2] { 0, 0 };
        private double[] fm_product_width = new double[2] { 0.0, 0.0 };
        private double[] fm_product_thin = new double[2] { 0.0, 0.0 };
        private bool fm_isSetRichBox = false;
        private bool fm_isShowCycleTime = false;

        private bool out_isAlarm = false;
        private void RunOutput()
        {
            switch (this.current_mode)
            {
                case MODE.MANUAL:
                    {
                        if (true == this.out_isGetManualSignal)
                        {
                            this.out_isGetManualSignal = false;

                            this.PLC.WriteM((int)MODE_IO.LtoC_StartToManual, this.plcOFF);
                            this.PLC.WriteM((int)MODE_IO.CtoL_ResponseManual, this.plcON);
                        }
                    }
                    break;
                case MODE.AUTO:
                    {
                        switch (this.current_process)
                        {
                            case PROCESS.NONE:
                                if (true == this.out_isGetAutoSignal)
                                {
                                    this.out_isGetAutoSignal = false;

                                    this.PLC.WriteM((int)MODE_IO.LtoC_StartToAuto, this.plcOFF);
                                    this.PLC.WriteM((int)MODE_IO.CtoL_ResponseAuto, this.plcON);
                                }

                                this.current_process = PROCESS.waitM3010ResetRun;
                                break;
                            case PROCESS.waitM3010ResetRun:
                                if (true == this.out_isResetRun)
                                {
                                    this.out_isResetRun = false;

                                    this.PLC.WriteM((int)IO.LtoC_CaptureRun, this.plcOFF);
                                    this.PLC.WriteM((int)IO.CtoL_CaptureDone, this.plcOFF);
                                    this.PLC.WriteM((int)IO.LtoC_ResetRun, this.plcOFF);
                                    this.PLC.WriteM((int)IO.CtoL_ResetDone, this.plcOFF);

                                    // set 3001~3007 = 0 & 3010~3017 = 0
                                    int Mcode = 3001;
                                    for (int i = 0; i < 2; i++)
                                    {
                                        for (int j = 0; j < 7; j++) { PLC.WriteM(Mcode + j, this.plcState_Default[i, j]); }
                                        Mcode = 3011;
                                    }

                                    this.current_process = PROCESS.setM3018ResetDone;
                                }
                                break;
                            case PROCESS.setM3018ResetDone:
                                if (true == this.out_isResetDone)
                                {
                                    this.out_isResetDone = false;

                                    this.PLC.WriteM((int)IO.CtoL_ResetDone, this.plcON);

                                    this.current_process = PROCESS.waitM3000CaptureRun;
                                }
                                break;
                            case PROCESS.waitM3000CaptureRun:
                                if (true == this.out_isCaptureRun)
                                {
                                    this.out_isCaptureRun = false;

                                    this.PLC.WriteM((int)IO.CtoL_ResetDone, this.plcOFF);
                                    this.PLC.WriteM((int)IO.LtoC_CaptureRun, this.plcOFF);

                                    this.seriaPort.Write("MA" + "\r");
                                    this.in_isStartToGetMeasureData = true;

                                    this.no_data_receive_count = 0;
                                    this.current_process = PROCESS.CaptureRun;
                                }
                                break;
                            case PROCESS.CaptureRun:
                                if (true == this.out_isStartToGetMeasureData)
                                {
                                    this.out_isStartToGetMeasureData = false;

                                    this.PLC.WriteM((3000 + (int)this.out_binResult[0]), this.plcON);
                                    this.PLC.WriteM(3010 + (int)this.out_binResult[1], this.plcON);

                                    this.sleep_count = 0;
                                    this.current_process = PROCESS.setM3008CaptureDone;

                                    // to display
                                    for (int i = 0; i < this.fm_bin_resutl.Length; i++)
                                    {
                                        this.fm_bin_resutl[i] = this.out_binResult[i];
                                        this.fm_product_width[i] = this.out_productWidth[i];
                                        this.fm_product_thin[i] = this.out_productThin[i];
                                    }
                                }
                                else
                                {
                                    this.seriaPort.Write("MA" + "\r");
                                    this.no_data_receive_count++;
                                }
                                break;
                            case PROCESS.setM3008CaptureDone:
                                if (true == this.out_isCaptureDone)
                                {
                                    this.out_isCaptureDone = false;
                                    this.PLC.WriteM((int)IO.CtoL_CaptureDone, this.plcON);

                                    this.sleep_count = 0;
                                    this.current_process = PROCESS.Finish;
                                }
                                break;
                            case PROCESS.Finish:
                                if (true == this.out_isFinish)
                                {
                                    this.out_isCaptureDone = false;
                                    this.fm_isSetRichBox = true;

                                    this.current_process = PROCESS.NONE;
                                }
                                break;
                            default:
                                break;
                        }

                        if (true == this.in_isCountCycleTime)
                        {
                            this.in_isCountCycleTime = false;

                            this.PLC.WriteM(3050, this.plcOFF);
                            if (true == this.in_isShowCycleTime)
                            {
                                this.fm_isShowCycleTime = this.in_isShowCycleTime;
                                this.in_isShowCycleTime = false;
                            }
                        }
                    }
                    break;
                default:
                    break;
            }

            // to normal display 
            this.fm_M3000_CaptureRun = this.in_M3000_CaptureRun;
            this.fm_M3008_CaptureDone = this.in_M3008_CaptureDone;
            this.fm_M3010_ResetRun = this.in_M3010_ResetRun;
            this.fm_M3018_ResetDone = this.in_M3018_ResetDone;

            // for touch PLC all time
            if (true == this.out_isAlarm) { PLC.WriteM(3020, "1"); }
            else { PLC.WriteM(3020, "0"); }
            this.out_isAlarm = !this.out_isAlarm;
        }
        #endregion

        #region Capture Function
        private uint[] binResult = new uint[2] { 0, 0 };
        private bool CaptureKeyenceData(out uint[] product_result, out double[] product_width, out double[] product_thin)
        {
            product_result = new uint[2] { 0, 0 };
            product_width = new double[2] { 0.0, 0.0 };
            product_thin = new double[2] { 0.0, 0.0 };

            //if (this.seriaPort.BytesToRead == 0)
            if (this.seriaPort.BytesToRead != 0)
            {
                // Initialize the array:
                product_width = new double[2] { 0, 0 };
                product_thin = new double[2] { 0, 0 };

                string data_from_keyence = this.seriaPort.ReadExisting();

                string[] temp_data = new string[10] { "0.0", "0.0", "0.0", "0.0", "0.0", "0.0", "0.0", "0.0", "0.0", "0.0" };
                string[] split = data_from_keyence.Split(',');
                int length = 10;
                if (split.Length < temp_data.Length) { length = split.Length; }
                for (int i = 0; i < split.Length; i++) { temp_data[i] = split[i]; }

                double value = 0;
                double[] temp_value = new double[4] { 0.0, 0.0, 0.0, 0.0 };
                for (int i = 0; i < 4; i++)
                {
                    if (true == double.TryParse(temp_data[i + 1], out value))
                    {
                        temp_value[i] = value;
                    }
                    else
                    {
                        return false;
                    }
                }
                product_width[0] = temp_value[0];
                product_thin[0] = temp_value[1];
                product_width[1] = temp_value[2];
                product_thin[1] = temp_value[3];
                
                // separate Bin 
                int current_ng_index = this.bin_NG_num - 1;

                for (int product_index = 0; product_index < 2; product_index++)
                {
                    for (int bin_index = 0; bin_index < this.bin_NG_num; bin_index++)
                    {
                        if ((bin_index != current_ng_index) && (true == bin_enable[bin_index]))
                        {
                            if ((this.bin_width[MIN, bin_index] <= product_width[product_index]) && (this.bin_width[MAX, bin_index] > product_width[product_index]) && (this.bin_thin[MIN, bin_index] <= product_thin[product_index]) && (this.bin_thin[MAX, bin_index] > product_thin[product_index]))
                            {
                                product_result[product_index] = (uint)(bin_index + 1);
                                bin_index = this.bin_NG_num;
                            }

                        }
                    }
                }
                

                //// separate Bin old
                //for (int i = 0; i < 2; i++)
                //{
                //    if ((product_width[i] >= this.min_width_1) && (product_width[i] < this.max_width_1))
                //    {
                //        if ((product_thin[i] >= this.min_thin_1) && (product_thin[i] < this.max_thin_1)) { product_result[i] = 1; }
                //        else if ((product_thin[i] >= this.min_thin_2) && (product_thin[i] < this.max_thin_2)) { product_result[i] = 2; }
                //        else if ((product_thin[i] >= this.min_thin_3) && (product_thin[i] < this.max_thin_3)) { product_result[i] = 3; }
                //        else { product_result[i] = 7; }
                //    }
                //    else if ((product_width[i] >= this.min_width_2) && (product_width[i] < this.max_width_2))
                //    {
                //        if ((product_thin[i] >= this.min_thin_1) && (product_thin[i] < this.max_thin_1)) { product_result[i] = 4; }
                //        else if ((product_thin[i] >= this.min_thin_2) && (product_thin[i] < this.max_thin_2)) { product_result[i] = 5; }
                //        else if ((product_thin[i] >= this.min_thin_3) && (product_thin[i] < this.max_thin_3)) { product_result[i] = 6; }
                //        else { product_result[i] = 7; }
                //    }
                //    else
                //    {
                //        product_result[i] = 7;
                //    }
                //}
                return true;
            }
            else
            {
                return false;
            }
        }
        #endregion

        #region Display Timer
        private void displayTimer_Tick(object sender, EventArgs e)
        {
            if (MODE.AUTO == this.current_mode)
            {
                this.operateModeTextBox.Text = "自動模式";
                this.operateModeTextBox.BackColor = Color.FromArgb(192, 255, 192);
            }
            else //if (MODE.MANUAL == this.current_mode)
            {
                this.operateModeTextBox.Text = "手動模式";
                this.operateModeTextBox.BackColor = SystemColors.Control;
            }

            if (this.plcON == this.fm_M3000_CaptureRun)
            {
                this.m3000CheckBox.Checked = true;
                this.m3000CheckBox.BackColor = Color.FromArgb(255, 192, 192);
            }
            else
            {
                this.m3000CheckBox.Checked = false;
                this.m3000CheckBox.BackColor = Color.FromArgb(255, 255, 192);
            }
            if (this.plcON == this.fm_M3008_CaptureDone)
            {
                this.m3008CheckBox.Checked = true;
                this.m3008CheckBox.BackColor = Color.FromArgb(255, 192, 192);
            }
            else
            {
                this.m3008CheckBox.Checked = false;
                this.m3008CheckBox.BackColor = Color.FromArgb(255, 255, 192);
            }
            if (this.plcON == this.fm_M3010_ResetRun)
            {
                this.m3010CheckBox.Checked = true;
                this.m3010CheckBox.BackColor = Color.FromArgb(255, 192, 192);
            }
            else
            {
                this.m3010CheckBox.Checked = false;
                this.m3010CheckBox.BackColor = Color.FromArgb(255, 255, 192);
            }
            if (this.plcON == this.fm_M3018_ResetDone)
            {
                this.m3018CheckBox.Checked = true;
                this.m3018CheckBox.BackColor = Color.FromArgb(255, 192, 192);
            }
            else
            {
                this.m3018CheckBox.Checked = false;
                this.m3018CheckBox.BackColor = Color.FromArgb(255, 255, 192);
            }

            this.resultOneTextBox.Text = this.fm_bin_resutl[0].ToString();
            this.resultTwoTextBox.Text = this.fm_bin_resutl[1].ToString();
            this.mCodeOneTextBox.Text = "M" + (3000 + fm_bin_resutl[0]).ToString();
            this.mCodeTwoTextBox.Text = "M" + (3010 + fm_bin_resutl[1]).ToString();

            if (false == this.PLC.isConnect)
            {
                this.connectPlcTextBox.Text = "PLC 尚未連線！";
                this.connectPlcTextBox.BackColor = Color.Fuchsia;
            }
            else
            {
                this.connectPlcTextBox.Text = "PLC 已連線！";
                this.connectPlcTextBox.BackColor = SystemColors.Control;
            }
            if (false == this.seriaPort.IsOpen)
            {
                this.connectComPortTextBox.Text = "量測儀器 尚未連線！";
                this.connectComPortTextBox.BackColor = Color.Fuchsia;
            }
            else
            {
                this.connectComPortTextBox.Text = "量測儀器 已連線！";
                this.connectComPortTextBox.BackColor = SystemColors.Control;
            }

            this.processStateTextBox.Text = this.current_process.ToString();

            if (true == this.fm_isSetRichBox)
            {
                this.fm_isSetRichBox = false;

                this.product_total_num+=2;
                this.totalNumTextBox.Text = this.product_total_num.ToString();

                this.dataRichTextBox.Text += System.DateTime.Now.ToString
                                             ("yyyy/MM/dd HH:mm:ss") + Environment.NewLine +
                                              "-- W1( " + this.fm_product_width[0] + " ) T1( " + this.fm_product_thin[0] + " ) =>{" + this.fm_bin_resutl[0].ToString() + "}." + Environment.NewLine +
                                              "    W2( " + this.fm_product_width[1] + " ) T2( " + this.fm_product_thin[1] + " ) =>{" + this.fm_bin_resutl[1].ToString() + "}." + Environment.NewLine + "\r\n";
            }

           if (true ==  this.fm_isShowCycleTime)
            {
                this.fm_isShowCycleTime = false;            
                //this.elapsedTimeTextBox.Text = (this.stop_watch.Elapsed.TotalMilliseconds / 1000.0).ToString("0.00000");
                this.elapsedTimeTextBox.Text = this.elapsed_time;
            }
        }

        private void dataRichTextBox_TextChanged(object sender, EventArgs e)
        {
            dataRichTextBox.SelectionStart = dataRichTextBox.TextLength;
            dataRichTextBox.ScrollToCaret();
        }
        #endregion
        
        #region Testing
        private void testM3010Button_Click(object sender, EventArgs e)
        {
            this.in_isResetRun= true;
        }

        private void testM3000Button_Click(object sender, EventArgs e)
        {
            this.in_isCaptureRun = true;
        }

        private void testM3040Button_Click(object sender, EventArgs e)
        {
            this.in_isGetAutoSignal = true;
            this.current_mode = MODE.AUTO;
            this.current_process = PROCESS.NONE;
        }

        private void testM3050Button_Click(object sender, EventArgs e)
        {
            this.in_isCountCycleTime = true;

            if (false == this.stop_watch.IsRunning)
            {
                this.stop_watch.Reset();    //碼表歸零
                this.stop_watch.Start();    //碼表開始計時
            }
            else
            {
                this.stop_watch.Stop();     //碼錶停止
                this.in_isShowCycleTime = true;
            }
        }

        #endregion

        #region Bin Eable Settings
        private void enableBinPanel_Paint(object sender, PaintEventArgs e)
        {
            int num = 0;
            string name = ((Panel)sender).Name.Substring(9, 1);
            if (true == int.TryParse(name, out num))
            {
                if (true == this.bin_enable[num-1]) { ((Panel)sender).BackColor = Color.Green; }
                else { ((Panel)sender).BackColor = SystemColors.Control; }
            }
        }

        private CheckBox[] enableBinCheckBox;
        private TextBox[] minWidthBinTextBox, maxWidthBinTextBox, minThinBinTextBox, maxThinBinTextBox;
        private Button[] setBinButton;
        private void enableBinObject(int index,bool state)
        {
            this.minWidthBinTextBox[index].Enabled = state;
            this.maxWidthBinTextBox[index].Enabled = state;
            this.minThinBinTextBox[index].Enabled = state;
            this.maxThinBinTextBox[index].Enabled = state;

            this.setBinButton[index].Enabled = state;
        }
        private void enableBinCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            int num = 0;
            string name = ((CheckBox)sender).Name.Substring(9, 1);
            if (true == int.TryParse(name, out num))
            {
                if ((num != (int)this.bin_NG_num) || (true == this.isNgSetting))
                {
                    this.bin_enable[num - 1] = ((CheckBox)sender).Checked;
                    this.enableBinObject(num - 1, ((CheckBox)sender).Checked);

                    this.SetBinMeasureData(true,false);
                }
                else
                {
                    ((CheckBox)sender).Checked = this.bin_enable[num - 1];
                }
            }
        }
        private bool isNgSetting = false;
        private void setNgNumButton_Click(object sender, EventArgs e)
        {
            this.bin_NG_num = (int)this.currentNgNumericUpDown.Value;
            this.isNgSetting = true;
            this.enableBinCheckBox[this.bin_NG_num - 1].Checked = true;
            this.isNgSetting = false;
            this.minWidthBinTextBox[this.bin_NG_num - 1].Text = "0.000";
            this.maxWidthBinTextBox[this.bin_NG_num - 1].Text = "0.000";
            this.minThinBinTextBox[this.bin_NG_num - 1].Text = "0.000";
            this.maxThinBinTextBox[this.bin_NG_num - 1].Text = "0.000";
            this.setBinButton[this.bin_NG_num - 1].PerformClick();
            this.SetBinMeasureData(false,false);
        }
        private void setBinButton_Click(object sender, EventArgs e)
        {
            int num = 0;
            string name = ((Button)sender).Name.Substring(6, 1);
            if (true == int.TryParse(name, out num))
            {
                double value = 0.0;

                if (true == double.TryParse(this.minWidthBinTextBox[num - 1].Text, out value))
                {
                    this.bin_width[MIN, num - 1] = value;
                    this.minWidthBinTextBox[num - 1].Text = value.ToString("#0.000");
                }
                if (true == double.TryParse(this.maxWidthBinTextBox[num - 1].Text, out value))
                {
                    this.bin_width[MAX, num - 1] = value;
                    this.maxWidthBinTextBox[num - 1].Text = value.ToString("#0.000");
                }
                if (true == double.TryParse(this.minThinBinTextBox[num - 1].Text, out value))
                {
                    this.bin_thin[MIN, num - 1] = value;
                    this.minThinBinTextBox[num - 1].Text = value.ToString("#0.000");
                }
                if (true == double.TryParse(this.maxThinBinTextBox[num - 1].Text, out value))
                {
                    this.bin_thin[MAX, num - 1] = value;
                    this.maxThinBinTextBox[num - 1].Text = value.ToString("#0.000");
                }

                this.SetBinMeasureData(false, true);
            }
        }


        #endregion

        #region Total Num
        private void clearTotalNumButton_Click(object sender, EventArgs e)
        {
            this.product_total_num = 0;
            this.product_date = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");

            this.totalNumTextBox.Text = this.product_total_num.ToString();
            this.totalNumTimeLabel.Text = this.product_date;

            this.SetTotalNumAndDate();
        }

        #endregion

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.current_mode == MODE.AUTO)
            {
                this.tabControl1.SelectedIndex = 0;
            }
        }



    }
}
