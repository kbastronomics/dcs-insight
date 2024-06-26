﻿using DCSInsight.Events;
using DCSInsight.JSON;
using DCSInsight.Misc;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DCSInsight.Interfaces;

namespace DCSInsight.Communication
{
    internal class TCPClientHandler : IDisposable, ICommandListener

    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly ConcurrentQueue<DCSAPI> _commandsQueue = new();
        private TcpClient? _tcpClient;
        private Thread? _clientThread;
        private bool _isRunning;
        private readonly string _host;
        private readonly string _port;
        public bool LogJSON { get; set; }
        private string _currentMessage = "";
        private volatile bool _responseReceived;

        public TCPClientHandler(string host, string port)
        {
            _host = host;
            _port = port;
            ICEventHandler.AttachCommandListener(this);
        }


        public void Dispose()
        {
            ICEventHandler.DetachCommandListener(this);
            _tcpClient?.Dispose();
            GC.SuppressFinalize(this);
        }

        public void RequestAPIList()
        {
            if (_tcpClient == null) return;

            try
            {
                if (!_tcpClient.Connected) return;

                Thread.Sleep(300);
                _tcpClient.GetStream().Write(Encoding.ASCII.GetBytes("SENDAPI\n"));
                Thread.Sleep(1000);

                var bytes = new byte[_tcpClient.Available];
                var bytesRead = _tcpClient.GetStream().Read(bytes);
                var msg = Encoding.ASCII.GetString(bytes);
                if (LogJSON) Logger.Info(msg);
                HandleAPIMessage(msg);
                Thread.Sleep(100);
            }
            catch (SocketException ex)
            {
                Logger.Error(ex);
                return;
            }
        }

        public void StartListening()
        {
            _isRunning = true;
            _clientThread = new Thread(ClientThread);
            _clientThread.Start();
        }

        private void ClientThread()
        {
            if (_tcpClient == null) return;

            ICEventHandler.SendConnectionStatus(_isRunning);
            _responseReceived = true;
            while (_isRunning)
            {
                try
                {
                    /* pear to the documentation on Poll:
                     * When passing SelectMode.SelectRead as a parameter to the Poll method it will return
                     * -either- true if Socket.Listen(Int32) has been called and a connection is pending;
                     * -or- true if data is available for reading;
                     * -or- true if the connection has been closed, reset, or terminated;
                     * otherwise, returns false
                     */

                    // Detect if client disconnected
                    if (_tcpClient.Client.Poll(0, SelectMode.SelectRead))
                    {
                        var buffer = new byte[1];
                        if (_tcpClient.Client.Receive(buffer, SocketFlags.Peek) == 0)
                        {
                            // Client disconnected
                            break;
                        }
                    }

                    if (!_tcpClient.Connected) break;

                    if (_commandsQueue.Count > 0 && _responseReceived)
                    {
                        if (!_commandsQueue.TryDequeue(out var dcsApi)) continue;

                        if (LogJSON) Logger.Info(JsonConvert.SerializeObject(dcsApi, Formatting.Indented));

                        _tcpClient.GetStream().Write(Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(dcsApi) + "\n"));
                        _responseReceived = false;
                    }

                    if (_tcpClient.Available <= 0) continue;

                    var bytes = new byte[_tcpClient.Available];
                    var bytesRead = _tcpClient.GetStream().Read(bytes);
                    var msg = Encoding.ASCII.GetString(bytes);
                    if (LogJSON) Logger.Info(msg);
                    HandleCommandMessage(msg);
                    Thread.Sleep(100);
                }
                catch (SocketException ex)
                {
                    Logger.Error(ex);
                    break;
                }
            }

            _isRunning = false;
            _tcpClient = null;
            ICEventHandler.SendConnectionStatus(_isRunning);
        }

        private void HandleCommandMessage(string str)
        {
            try
            {
                if (str.Contains("\"returns_data\":") && str.EndsWith("}")) // regex?
                {
                    DCSAPI? dcsApi;
                    try
                    {
                        dcsApi = JsonConvert.DeserializeObject<DCSAPI>(_currentMessage + str);
                    }
                    catch (Exception e)
                    {
                        _currentMessage = "";
                        _responseReceived = true;
                        ICEventHandler.SendCommsErrorMessage("Error parsing JSON (API)", e);
                        return;
                    }

                    if (dcsApi == null) return;

                    _currentMessage = "";
                    ICEventHandler.SendCommandData(dcsApi);
                    _responseReceived = true;
                }
                else
                {
                    _currentMessage += str;
                }
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex, "HandleMessage()");
            }
        }

        private void HandleAPIMessage(string str)
        {
            try
            {
                List<DCSAPI>? dcsAPIList;
                try
                {
                    dcsAPIList = JsonConvert.DeserializeObject<List<DCSAPI>>(str);
                }
                catch (Exception e)
                {
                    _responseReceived = true;
                    ICEventHandler.SendCommsErrorMessage("Error parsing JSON (API List)", e);
                    return;
                }

                if (dcsAPIList == null) return;

                ICEventHandler.SendAPIData(dcsAPIList);
                _responseReceived = true;
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex, "HandleAPIMessage()");
            }
        }


        public void Connect()
        {
            if (_isRunning) return;

            try
            {
                IPEndPoint serverEndPoint;

                if (_host != "127.0.0.1")
                {
                    serverEndPoint = new(IPAddress.Parse(_host), Convert.ToInt32(_port));
                }
                else
                {
                    serverEndPoint = new(IPAddress.Loopback, Convert.ToInt32(_port));
                }
                _isRunning = false;
                _tcpClient = new TcpClient();
                _tcpClient.Connect(serverEndPoint);
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        public void Disconnect()
        {
            try
            {
                _isRunning = false;
                _tcpClient?.Close();
                _tcpClient = null;
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }

        public void AddCommand(DCSAPI dcsApi)
        {
            _commandsQueue.Enqueue(dcsApi);
        }

        public void SendCommand(SendCommandEventArgs args)
        {
            try
            {
                _commandsQueue.Enqueue(args.APIObject);
            }
            catch (Exception ex)
            {
                Common.ShowErrorMessageBox(ex);
            }
        }
    }
}
