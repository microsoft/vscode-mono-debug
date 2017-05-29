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

        public XamarinConnectionProvider(int port)
        {
            _port = port;
        }

        public IAsyncResult BeginConnect(DebuggerStartInfo dsi, AsyncCallback callback)
        {
            return XamarinVirtualMachineManager.BeginConnect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), _port), null, callback);
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