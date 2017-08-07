using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Mono.Debugger.Soft;
using Mono.Debugging.Client;
using Mono.Debugging.Soft;


namespace VSCodeDebug
{
    public class XamarinConnectionProvider : ISoftDebuggerConnectionProvider
    {
        private readonly int _port;
        private readonly StreamReader _console;

        public XamarinConnectionProvider(int port, StreamReader console = null)
        {
            _port = port;
            _console = console;
        }

        public IAsyncResult BeginConnect(DebuggerStartInfo dsi, AsyncCallback callback)
        {
            IAsyncResult result;
            var endPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), _port);
            if (_console != null)
            {
                result = XamarinVirtualMachineManager.BeginConnect(endPoint, _console, callback);
            }
            else
            {
                result = XamarinVirtualMachineManager.BeginConnect(endPoint, endPoint, callback);
            }
            return result;
        }

        public void CancelConnect(IAsyncResult result)
        {
            XamarinVirtualMachineManager.CancelConnection(result);
        }

        public void EndConnect(IAsyncResult result, out VirtualMachine vm, out string appName)
        {
            vm = XamarinVirtualMachineManager.EndConnect(result);
            appName = null;
        }

        public bool ShouldRetryConnection(Exception ex)
        {
            return false;
        }
    }
}