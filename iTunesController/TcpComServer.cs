using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Threading;
using System.Collections;
using System.Timers;
using System.IO;

namespace iTunesController
{
    class TcpComServer
    {
        TcpListener _listener;
        volatile Socket _s;
        volatile AsyncCallback _acceptSocket;
        NetworkStreamReader _nsr;
        NetworkStreamSender _nss;
        volatile NetworkStream _ns;
        System.Timers.Timer _heartbeatTimer;
        byte[] _keepAlive;
        private bool _stopping;
        private int _prevPort;

        const int _heartbeatInterval = 1000;
        const int _sendTimeoutInterval = 25;   //known to work at 250, was previously 250
        const int _endOfPacketChar = (int)'\n';

        public delegate void PacketReadyEventHandler(string packet);
        public event PacketReadyEventHandler PacketReady;

        public delegate void ConnectionStateChangeEventHander(ConnectionState connectionState, string description);
        public event ConnectionStateChangeEventHander ServerConnectionStateChange;

        public delegate void ConnectionEventHandler(ConnectionEventType connectionEvent, string data);
        public event ConnectionEventHandler ConnectionEvent;

        public enum ConnectionState
        {
            Disconnected,
            Listening,
            Connected
        };

        public enum ConnectionEventType
        {
            packetSent,
            packetReceived
        };

        public TcpComServer()
        {
            _keepAlive = new byte[1];
            _keepAlive[0] = 0;

            _stopping = false;
        }

        public void Start(int port)
        {
            _prevPort = port;
            IPAddress addr = IPAddress.Parse(GetLocalIP());
            _listener = new TcpListener(addr, port);

            Debug.Print("attempting to start");

            _listener.Start();
               
            
             _stopping = false;
            BeginSocketAccepting();
        }

        private void BeginSocketAccepting()
        {
            _acceptSocket = new AsyncCallback(AcceptSocketCallback);

            _listener.BeginAcceptSocket(_acceptSocket, _listener);

            Debug.Print("started server");
            ServerConnectionStateChange(ConnectionState.Listening, "Started listening");
            
        }

        public char GetEndOfPacketChar()
        {
            return (char)_endOfPacketChar;
        }

        private void AcceptSocketCallback(IAsyncResult ar)
        {
            //if (_s != null)
            //{
            //    if (_s.Connected)
            //    {
            //        //We need to get rid of the other socket before accepting this one.
            //        Restart("new incomming connection.");
            //    }
            //}
            
            //Accept the connection and store a pointer to the connected socket in _s
            TcpListener myListener = (TcpListener)ar.AsyncState;

            _s = myListener.EndAcceptSocket(ar);
            //myListener.Stop();
            _s.SendTimeout = _sendTimeoutInterval;  //testing to disable, result is bad, make this line remain UNcommented, works at 250

            _ns = new NetworkStream(_s);
            
            Debug.Print(_s.RemoteEndPoint.AddressFamily.ToString() + " has connected");

            //Start listening for data from the socket.
            _nsr = new NetworkStreamReader(_ns, _endOfPacketChar);
            _nsr.PacketReady += new NetworkStreamReader.PacketReadyEventHandler(_nsr_PacketReady);
            _nsr.NeedToRestart += new NetworkStreamReader.NeedToRestartEventHandler(_nsr_NeedToRestart);
            new Thread(_nsr.DoWork).Start();

            //And start the thread for sending packets
            _nss = new NetworkStreamSender(_ns, _endOfPacketChar);
            _nss.ComFailure += new NetworkStreamSender.ComFailureEventHandler(_nss_ComFailure);
            _nss.PacketSent += new NetworkStreamSender.PacketSentEventHandler(_nss_PacketSent);
            new Thread(_nss.DoWork).Start();

            //Start the time out packet.  If the socket was not able to send this packet, the client has disconnected.
            _heartbeatTimer = new System.Timers.Timer(_heartbeatInterval);
            _heartbeatTimer.Elapsed += new ElapsedEventHandler(_timeout_Elapsed);
            _heartbeatTimer.Start();

            //welcome message
            SendPacket("welcome");
            Debug.Print(_s.LocalEndPoint.AddressFamily.ToString());

            ServerConnectionStateChange(ConnectionState.Connected, _s.LocalEndPoint.AddressFamily.ToString() + " has connected.");



        }

        private void _nss_PacketSent(string data)
        {
            ConnectionEvent(ConnectionEventType.packetSent, data);
        }



        private void _nss_ComFailure()
        {
 	        Restart("Stopping server: NetworkStreamSend failure");
        }

        private void  _nsr_NeedToRestart()
        {
 	        Restart("Stopping server: NetworkStreamRead failure");
        }
        
        public void SendPacket(string packet)
        {
            if (!_stopping)
            {
                _nss.SendPacket(packet);
            }
        }

        private void _timeout_Elapsed(object sender, ElapsedEventArgs e)
        {
            //Send the time out packet.
            SendPacket("watchdog");
        }

        private void Restart(string reason)
        {
            Debug.Print("got to Restart");
            Stop(reason);

            //BeginSocketAccepting();
            Start(_prevPort);
        }

        public void Stop(string reason)
        {
            if (!_stopping)
            {
                _stopping = true;
                
                //Kills the current connection, does not stop accepting new connections
                Debug.Print("Stopping server: " + reason);
                

                if (_heartbeatTimer != null)
                {
                    _heartbeatTimer.Stop();
                    _heartbeatTimer.Dispose();
                }

                _nss._packetsToSend.Clear();



                //_ns.Dispose();
                if (_nsr != null)
                    _nsr.RequestStop();

                if (_nss != null)
                    _nss.RequestStop();

                if (_ns != null)
                {

                    _ns.Close();
                    
                    //_ns.Dispose();
                }

                try
                {
                    //_s.Shutdown(SocketShutdown.Both);
                    _s.Disconnect(false);
                    _s.Close();

                    _listener.Stop();
                    
                    
                    
                    
                }
                catch (Exception)
                {

                }

                ServerConnectionStateChange(ConnectionState.Disconnected, reason);

                Debug.Print("finished shutdown method");

                _stopping = false;
            }
        }

        private void _nsr_PacketReady()
        {
            string packet = _nsr._packetQueue.Dequeue();

            Debug.Print("packet just received: " + packet);
            
            //need to determine if we need to disconnect
            if (packet == "disconnecting")
            {
                //Debug.Print("Client is disconnecting, will restart...");

                //_clientDisconnect = new ClientIsDisconnecting(_ns, _endOfPacketChar);
                //_clientDisconnect.Disconnected += new ClientIsDisconnecting.FinishedDisconnectMessage(_clientDisconnect_Disconnected);

                //new Thread(_clientDisconnect.DoWork).Start();
                Debug.Print("Client has disconnected.");

                Restart("client has disconnected");

            }
            else
            {
                PacketReady(packet);
            }
        }

        private class NetworkStreamSender
        {
            volatile NetworkStream _ns;
            public volatile Queue<string> _packetsToSend;
            private bool _requestStop;
            private int _endOfPacketChar;

            public delegate void ComFailureEventHandler();
            public event ComFailureEventHandler ComFailure;

            public delegate void PacketSentEventHandler(string data);
            public event PacketSentEventHandler PacketSent;

            public volatile EventWaitHandle _ewh;

            private EventWaitHandle _requestStopWaitHandle;

            public NetworkStreamSender(NetworkStream ns, int endOfPacketChar)
            {
                _ns = ns;
                _endOfPacketChar = endOfPacketChar;
                _packetsToSend = new Queue<string>();
                _requestStop = false;

                _ewh = new EventWaitHandle(true, EventResetMode.ManualReset);

                _requestStopWaitHandle = new EventWaitHandle(true, EventResetMode.ManualReset);
            }

            public void SendPacket(string packet)
            {
                _packetsToSend.Enqueue(packet);
                ProcessQueue();
            }

            public void ProcessQueue()
            {
                _ewh.Set();
            }

            public void DoWork()
            {
                while (!_requestStop)
                {
                    if (_packetsToSend.Count > 0)
                    {
                        while (_packetsToSend.Count > 0)
                        {

                            string buff = "";
                            try
                            {
                                buff = _packetsToSend.Dequeue();
                                Debug.Print("attempting to send: " + buff);
                            }
                            catch (System.InvalidOperationException ioe)
                            {
                                
                                //do nothing, nothing in the buffer
                            }

                            try
                            {
                                if (buff != "")
                                {
                                    _ns.Write(Encoding.ASCII.GetBytes(buff), 0, buff.Length);
                                    _ns.WriteByte((byte)_endOfPacketChar);
                                    _ns.Flush();
                                    PacketSent(buff);
                                }
                            }
                            catch (Exception e)
                            {
                                //Something went wrong.  Just assume to reset the server.
                                ComFailure();
                                Debug.Print(e.Message);

                            }

                            //Done writing the packet, now write the end of packet character

                        }
                    }
                    else
                    {
                        //No more packets waiting, cause the thread to block until a new packet is signaled to be ready
                        _ewh.Reset();
                    }

                    //Done writing packets, wait
                    //Thread.Sleep(1);


                    _ewh.WaitOne();

                }

                //done
                //This is used to ensure that the RequestStop() method blocks until this therad, the sending thread, successfully completes.
                //Ensures this thread completes before the method RequestStop() completes
                _requestStopWaitHandle.Set();

            }

            public void RequestStop()
            {
                if (!_requestStop)
                {
                    _requestStop = true;
                    
                    _packetsToSend.Clear();
                    
                    ProcessQueue();
                    //_requestStopWaitHandle.Reset();
                    _requestStopWaitHandle.WaitOne();
                    Debug.Print("done waiting");

                    //_requestStop = false;
                }

            }
        }

        private class NetworkStreamReader
        {
            volatile bool _shouldStop;
            volatile NetworkStream _ns;
            byte[] _buff;
            string _strBuff;
            int _endOfPacketChar;

            public delegate void PacketReadyEventHandler();
            public event PacketReadyEventHandler PacketReady;

            public delegate void NeedToRestartEventHandler();
            public event NeedToRestartEventHandler NeedToRestart;

            public volatile Queue<string> _packetQueue;

            private EventWaitHandle _requestStopWaitHandle;
            
            public NetworkStreamReader(NetworkStream ns, int endOfPacketChar)
            {
                _shouldStop = false;
                _ns = ns;
                _strBuff = "";
                _endOfPacketChar = endOfPacketChar;
                _packetQueue = new Queue<string>();

                _buff = new byte[1];

                _requestStopWaitHandle = new EventWaitHandle(true, EventResetMode.ManualReset);
            }

            public void DoWork()
            {
                bool readSuccess = false;

                while (!_shouldStop)
                {
                    //the loop

                    try
                    {
                        //_ns.Read(_buff, 0, 1);
                        _ns.Read(_buff, 0, 1);
                        readSuccess = true;
                    }
                    catch (IOException)
                    {
                        //RequestStop();
                        if (!_shouldStop)
                            NeedToRestart();
                        
                        //break;

                    }
                    catch (System.ObjectDisposedException)
                    {
                        break;
                    }

                    if (readSuccess)
                    {

                        if (_buff[0] == _endOfPacketChar)
                        {
                            if (_strBuff.Length > 0)
                            {
                                _packetQueue.Enqueue(_strBuff);

                                _strBuff = "";

                                //Raise event that a packet is ready.
                                PacketReady();
                                //Debug.Print("raised event packet ready");
                            }

                        }
                        else
                        {
                            _strBuff = _strBuff + (char)_buff[0];

                        }

                        readSuccess = false;
                    }

                }

                //done
                _requestStopWaitHandle.Set();
            }

            public void RequestStop()
            {
                if (!_shouldStop)
                {

                    _shouldStop = true;
                    //_requestStopWaitHandle.Reset();
                    _requestStopWaitHandle.WaitOne();

                    //_shouldStop = false;
                }

                
            }
        }

        public void Finish()
        {
            _s.Close();
            _listener.Stop();
            _nsr.RequestStop();
            _nss.RequestStop();
            _ns.Close();
            _ns.Dispose();
            _heartbeatTimer.Enabled = false;
            _heartbeatTimer.Dispose();
        }

        private string GetLocalIP()
        {
            IPHostEntry host;
            string localIP = "?";
            host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily.ToString() == AddressFamily.InterNetwork.ToString())
                {
                    localIP = ip.ToString();
                }
            }
            return localIP;
        }
    }
}
