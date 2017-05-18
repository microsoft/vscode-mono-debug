using Mono.Debugging.Soft;

namespace VSCodeDebug
{
    public class XamarinDebuggerArgs : SoftDebuggerStartArgs
    {
        public override ISoftDebuggerConnectionProvider ConnectionProvider { get; }

        public XamarinDebuggerArgs(int port)
        {
            ConnectionProvider = new XamarinConnectionProvider(port);
        }
    }
}