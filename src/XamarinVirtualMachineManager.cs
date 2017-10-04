using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Remoting.Messaging;
using System.Text;
using Mono.Debugger.Soft;

namespace VSCodeDebug
{

	public static class XamarinVirtualMachineManager
	{
		private const string START_DEBUGGER_COMMAND = "start debugger: sdb";
		private const string CONNECT_STDOUT_COMMAND = "connect stdout";

		private delegate VirtualMachine ConnectWithConsoleOutputCallback (Socket dbg_sock, IPEndPoint dbg_ep, StreamReader console, TextWriter logWriter); 

		private static VirtualMachine ConnectWithConsoleOutput(Socket dbg_sock, IPEndPoint dbg_ep, StreamReader console, TextWriter logWriter = null) {
			dbg_sock.Connect (dbg_ep);
			SendCommand(dbg_sock, START_DEBUGGER_COMMAND);
			Connection transport = new XamarinTcpConnection (dbg_sock, logWriter);
			return VirtualMachineManager.Connect (transport, console, null);
		}

		private static void SendCommand(Socket socket, string command)
		{
			byte[] commandBin = System.Text.Encoding.ASCII.GetBytes(command);
			byte[] commandLenght = new byte[] { (byte)commandBin.Length };
			socket.Send(commandLenght, 0, commandLenght.Length, SocketFlags.None);
			socket.Send(commandBin, 0, commandBin.Length, SocketFlags.None);
		}

		public static IAsyncResult BeginConnect(IPEndPoint debugEndPoint, StreamReader console, AsyncCallback callback, TextWriter logWriter = null) {
			Socket debugSocket = null;
			debugSocket = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			ConnectWithConsoleOutputCallback c = new ConnectWithConsoleOutputCallback (ConnectWithConsoleOutput);
			return c.BeginInvoke (debugSocket, debugEndPoint, console, logWriter, callback, debugSocket);
		}

		public static VirtualMachine EndConnect(IAsyncResult asyncResult) {
			if (asyncResult == null)
				throw new ArgumentNullException ("asyncResult");

			if (!asyncResult.IsCompleted)
				asyncResult.AsyncWaitHandle.WaitOne ();

			AsyncResult result = (AsyncResult) asyncResult;
			ConnectWithConsoleOutputCallback cb = (ConnectWithConsoleOutputCallback) result.AsyncDelegate;
			return cb.EndInvoke(asyncResult);
		}

		public static void CancelConnection(IAsyncResult asyncResult)
		{
			((IDisposable) asyncResult.AsyncState).Dispose();
		}
	}
}
