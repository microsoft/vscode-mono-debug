using System;
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
            throw new NotImplementedException();
        }

        public void CancelConnect(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public void EndConnect(IAsyncResult result, out VirtualMachine vm, out string appName)
        {
            throw new NotImplementedException();
        }

        public bool ShouldRetryConnection(Exception ex)
        {
            throw new NotImplementedException();
        }
    }
}