using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.FlightSimulator.SimConnect;
using System.Runtime.InteropServices;

using System.Net;
using System.Net.Sockets;

using System.IO;
using System.IO.Ports;


namespace FSXConnectCS
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.Log("Welcome FSXConnect CS");
        }

        private void buttonStart_Click(object sender, EventArgs e)
        {
            this.Init();
        }

        private void Log(string msg)
        {
            textBox1.Text += msg;
            textBox1.Text += Environment.NewLine;
            
            textBox1.Select(textBox1.Text.Length, 0);
            textBox1.ScrollToCaret();
        }

        // User-defined win32 event
        const int WM_USER_SIMCONNECT = 0x0402;
        // SimConnect object
        SimConnect simconnect = null;
        
        protected override void DefWndProc(ref Message m)
        {
            if (m.Msg == WM_USER_SIMCONNECT)
            {
                if (simconnect != null)
                    simconnect.ReceiveMessage();
            }
            else
            {
                base.DefWndProc(ref m);
            }
        }

        // UDP property
        static int udpPort = 1234;
        UdpClient udpClient = null;
        
        private byte udpbyte = 0;
        IPEndPoint remoteEp = null;

        private void Init()
        {
            // simconnect
            if (simconnect == null)
            {
                try
                {
                    simconnect = new SimConnect("FSXConnectCS", this.Handle, WM_USER_SIMCONNECT, null, 0);
                    this.InitClientEvent();

                }
                catch (COMException ex)
                {
                    this.Log("Unable to connect to FSX " + ex.Message);
                }
            }

            // udp open
            if (udpClient == null)
            {
                try
                {
                    IPEndPoint hostEp = GetHostEndPoint();
                    udpClient = new UdpClient(hostEp);
                    string addr = udpClient.Client.LocalEndPoint.ToString();
                    this.Log("Udp Host : " + addr);
                    backgroundWorker1.RunWorkerAsync();
                }
                catch(Exception e)
                {
                    this.Log(e.Message);
                }
            }

            // serial open
            if (serialPort1.IsOpen == false)
            {
                try
                {
                    string portname = GetSerialPortName();
                    if (portname != null)
                    {
                        serialPort1.PortName = portname;
                        serialPort1.Open();
                        this.Log("Serial open " + serialPort1.PortName);
                    }
                }
                catch(Exception e)
                {
                    this.Log(e.Message);
                }
            }

        }

        private IPEndPoint GetHostEndPoint()
        {
            UdpClient u = new UdpClient();
            u.Connect("www.contoso.com", 11000);
            IPEndPoint ep = (IPEndPoint)(u.Client.LocalEndPoint);
            ep.Port = udpPort;
            u.Close();
            return ep;
        }

        private string GetSerialPortName()
        {
            string[] ports = SerialPort.GetPortNames();
            if (ports.Length > 0)
                return ports[0];

            return null;
        }

        // SimConnect Events
        // clientEvent : any custom id, for this app. Enum
        // simEvent : FSX declared event, string type
        
        enum NOTIFICATION_GROUPS
        {
            GROUP0,
        }

        private void addSimEvent(CLIENT_EVENTS clientEvent)
        {
            string simEvent = clientEvent.ToString();
            simconnect.MapClientEventToSimEvent(clientEvent, simEvent);
            simconnect.AddClientEventToNotificationGroup(NOTIFICATION_GROUPS.GROUP0, clientEvent, false);
        }
        private void sendSimEvent(CLIENT_EVENTS clientEvent)
        {
            if (simconnect == null)
                return;

            simconnect.TransmitClientEvent(0, clientEvent, 0, NOTIFICATION_GROUPS.GROUP0, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        }
        private void InitClientEvent()
        {
            try
            {
                // listen to connect and quit msgs
                simconnect.OnRecvOpen += new SimConnect.RecvOpenEventHandler(simconnect_OnRecvOpen);
                simconnect.OnRecvQuit += new SimConnect.RecvQuitEventHandler(simconnect_OnRecvQuit);
                // listen to exceptions
                simconnect.OnRecvException += new SimConnect.RecvExceptionEventHandler(simconnect_OnRecvException);
                // listen to events
                simconnect.OnRecvEvent += new SimConnect.RecvEventEventHandler(simconnect_OnRecvEvent);
                // set the group priority
                simconnect.SetNotificationGroupPriority(NOTIFICATION_GROUPS.GROUP0, SimConnect.SIMCONNECT_GROUP_PRIORITY_HIGHEST);

                // add event
                foreach (CLIENT_EVENTS ce in Enum.GetValues(typeof(CLIENT_EVENTS)))
                {
                    addSimEvent(ce);
                }

            }
            catch (COMException ex)
            {
                this.Log(ex.Message);
            }
        }

        void simconnect_OnRecvOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            this.Log("Connected to FSX");
        }

        // The case where the user closes FSX
        void simconnect_OnRecvQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            this.Log("FSX has exited");
            if (simconnect != null)
            {
                // Dispose serves the same purpose as SimConnect_Close()
                simconnect.Dispose();
                simconnect = null;
                this.Log("Connection closed");
            }
        }

        void simconnect_OnRecvException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            this.Log("Exception received: " + data.dwException);
        }

        void simconnect_OnRecvEvent(SimConnect sender, SIMCONNECT_RECV_EVENT recEvent)
        {
            switch (recEvent.uEventID)
            {
                case (uint)CLIENT_EVENTS.COM_RADIO_WHOLE_DEC:
#if DEBUG
                    this.Log("COM_RADIO_WHOLE_DEC");
#endif
                    break;
                

            }
        }


        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            while (true)
            {
                IPEndPoint ep = new IPEndPoint(IPAddress.Any, udpPort);
                byte[] buffer = udpClient.Receive(ref ep);
                remoteEp = ep;
                udpbyte = buffer[0];
                this.Invoke(new EventHandler(udpReceived));
            }
        }

        private void udpReceived(object sender, EventArgs e)
        {
            if (Enum.IsDefined(typeof(CLIENT_EVENTS), (int)udpbyte))
            {
#if DEBUG
                this.Log(udpbyte.ToString());
#endif
                sendSimEvent((CLIENT_EVENTS)udpbyte);
            }
        }

        private void udpSend(string msg)
        {
            if (remoteEp == null)
                return;

            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            udpClient.Send(buffer, buffer.Length, remoteEp);
        }

        private void serialPort1_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            this.Invoke(new EventHandler(serialReceived));
        }

        private void serialReceived(object sender, EventArgs e)
        {
            
            int b = serialPort1.ReadByte();
            if (Enum.IsDefined(typeof(CLIENT_EVENTS), b))
            {
                this.Log(b.ToString());
                sendSimEvent((CLIENT_EVENTS)b);
            }
            int count = serialPort1.BytesToRead;
            this.Log("byte:" + count.ToString());
        }

        private void serialSend(string msg)
        {
            if (serialPort1.IsOpen == false)
            {
                return;
            }

            serialPort1.Write(msg);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            // test
            sendSimEvent(CLIENT_EVENTS.COM1_TRANSMIT_SELECT);
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // test
            sendSimEvent(CLIENT_EVENTS.COM2_TRANSMIT_SELECT);
        }
    }
}
