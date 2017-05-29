using System.IO;
using System.Net;
using System.Net.Sockets;
using Mono.Debugger.Soft;

namespace VSCodeDebug
{
    class XamarinTcpConnection : Connection
	{
		Socket socket;

		internal XamarinTcpConnection (Socket socket, TextWriter logWriter)
			: base (logWriter)
		{
			this.socket = socket;
			//socket.SetSocketOption (SocketOptionLevel.IP, SocketOptionName.NoDelay, 1);
		}
		
		internal EndPoint EndPoint {
			get {
				return socket.RemoteEndPoint;
			}
		}
		
		protected override int TransportSend (byte[] buf, int buf_offset, int len)
		{
			return socket.Send (buf, buf_offset, len, SocketFlags.None);
		}
		
		protected override int TransportReceive (byte[] buf, int buf_offset, int len)
		{
			return socket.Receive (buf, buf_offset, len, SocketFlags.None);
		}
		
		protected override void TransportSetTimeouts (int send_timeout, int receive_timeout)
		{
			socket.SendTimeout = send_timeout;
			socket.ReceiveTimeout = receive_timeout;
		}
		
		protected override void TransportClose ()
		{
			socket.Close ();
		}
	}
}