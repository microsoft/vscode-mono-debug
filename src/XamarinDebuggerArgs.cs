using Mono.Debugging.Soft;
using System.IO;

namespace VSCodeDebug
{
    public class XamarinDebuggerArgs : SoftDebuggerStartArgs
    {
        public override ISoftDebuggerConnectionProvider ConnectionProvider { get; }

        public XamarinDebuggerArgs(int port, StreamReader deviceConsole = null)
        {
            ConnectionProvider = new XamarinConnectionProvider(port, deviceConsole);
        }
    }
}