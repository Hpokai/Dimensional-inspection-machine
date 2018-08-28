using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;//for running threads
using System.Net.Sockets;//use this namespace for sockets

namespace B1_Cap
{
    class PLCConn
    {
        public string IP { get; set; }
        public int Port { get; set; }
        public bool isConnect;
        public PLCConn(string _IP, int _Port)
        {
            IP = _IP;
            Port = _Port;
        }


        //宣告網路資料流變數2
        NetworkStream myNetworkStream;
        //宣告 Tcp 用戶端物件
        TcpClient myTcpClient;
     //   Thread Thread_ConnHandle;
        Thread Thread_ConnectState;
        public string RecieveData;

        public bool ConnectPLC()   //如果連線成功則ConnectPLC(true) 失敗則ConnectPLC(false);
        {
            myTcpClient = new TcpClient();
            try
            {
                //測試連線至遠端主機
                myTcpClient.Connect(IP, Port);
                //建立網路資料流
                myNetworkStream = myTcpClient.GetStream();
                Thread_ConnectState = new Thread(ConnectState);
                Thread_ConnectState.Start();
                isConnect = true;
                return true;
            }
            catch
            {
                isConnect = false;
                return false;
            }
        }
        void ConnectState()
        {
            byte[] testRecByte = new byte[1];
            try
            {
                while (myTcpClient.Connected)
                {
                    if (myTcpClient.Client.Receive(testRecByte, SocketFlags.Peek) == 0)
                        break;
                    Thread.Sleep(100);
                    isConnect = true;
                }
                isConnect = false;
            }
            catch
            {
                isConnect = false;
            }
        }
        /// <summary>讀取m值
        /// 
        /// </summary>
        /// <param name="Start">起始讀取位置</param>
        /// <param name="BSize">讀取大小</param>
        /// <returns></returns>
        public string ReadM(int Start, int BSize)
        {
            string MHeader = "500000ff03ff000018001004010001M*";
            string MStart = Start.ToString().PadLeft(6, '0');
            string MSize = Convert.ToString(BSize, 16).PadLeft(4, '0');
            WriteData(MHeader + MStart + MSize);
            ReadData();

            return TakeM(RecieveData);
        }
        public bool WriteM(int start, string M)
        {
            string MHeader = "500000ff03ff00";
            int intChangeAmount = M.Length + 24;
            string strAmount = Convert.ToString(intChangeAmount, 16).PadLeft(4, '0').ToUpper();
            string MCommand = "001014010001M*";
            string MStart = start.ToString().PadLeft(6, '0');
            string MSize = Convert.ToString(M.Length, 16).PadLeft(4, '0').ToUpper();
            string aaa = MHeader + strAmount + MCommand + MStart + MSize + M;
            if (!WriteData(MHeader + strAmount + MCommand + MStart + MSize + M))
                return false;
            return ReadData();
        }
        //讀取資料
        bool ReadData()
        {
            try
            {
                //從網路資料流讀取資料
                
                int bufferSize = myTcpClient.ReceiveBufferSize;
                byte[] myBufferBytes = new byte[bufferSize];
                myNetworkStream.Read(myBufferBytes, 0, bufferSize);
                //取得資料並且解碼文字
                RecieveData = (Encoding.ASCII.GetString(myBufferBytes, 0, bufferSize));
                return true;
            }
            catch
            {
                return false;
            }
        }
        //寫入資料
        bool WriteData(string SSS)
        {
            //將字串轉 byte 陣列，使用 ASCII 編碼
            
            Byte[] myBytes = Encoding.ASCII.GetBytes(SSS);
            try
            {
                myNetworkStream.Write(myBytes, 0, myBytes.Length);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
        public bool WaitMChange(int M, string status, int TimeOut_minsec)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Reset();
            sw.Start(); 

            while (true)
            {
                string a = ReadM(M, 1);

                if (a == status)
                    return true;

                if (sw.ElapsedMilliseconds > TimeOut_minsec)
                {
                    return false;
                }

                System.Threading.Thread.Sleep(1);
            }

        }
        //去除無用資訊
        string TakeM(string MS)
        {
            if (MS == null)
                return "";
            string tx = MS.Replace("\0", string.Empty);
            const int startNum = 22;
            return tx.Substring(startNum, tx.Length - startNum);
        }
        public void DisConnect()
        {

            if (myNetworkStream != null)
                myNetworkStream.Close();
            myTcpClient.Close();

        }
    }
}
