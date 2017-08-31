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

		private delegate VirtualMachine ConnectWithNetworkOutputCallback (Socket dbg_sock, Socket con_sock, IPEndPoint dbg_ep, IPEndPoint con_ep, TextWriter logWriter); 
		private delegate VirtualMachine ConnectWithConsoleCallback (Socket dbg_sock, IPEndPoint dbg_ep, StreamReader console, TextWriter logWriter); 

		private static VirtualMachine ConnectWithNetworkOutput (Socket dbg_sock, Socket con_sock, IPEndPoint dbg_ep, IPEndPoint con_ep, TextWriter logWriter = null) {
			if (con_sock != null) {
				try
				{
					con_sock.Connect(con_ep);
					SendCommand(con_sock, CONNECT_STDOUT_COMMAND);
				}
				catch (Exception) {
					try {
						dbg_sock.Close ();
					} catch { }
					throw;
				}
			}

			try {
				dbg_sock.Connect (dbg_ep);
				SendCommand(dbg_sock, START_DEBUGGER_COMMAND);
			} catch (Exception) {
				if (con_sock != null) {
					try {
						con_sock.Close ();
					} catch { }
				}
				throw;
			}

			Connection transport = new XamarinTcpConnection (dbg_sock, logWriter);
			StreamReader console = con_sock != null ? new StreamReader (new NetworkStream (con_sock)) : null;

			return VirtualMachineManager.Connect (transport, console, null);
		}

		private static VirtualMachine ConnectWithConsole (Socket dbg_sock, IPEndPoint dbg_ep, StreamReader console, TextWriter logWriter = null)
		{
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

		public static IAsyncResult BeginConnect (IPEndPoint debugEndPoint, IPEndPoint outputEndPoint, AsyncCallback callback, TextWriter logWriter = null) {
			Socket debugSocket = null;
			Socket outputSocket = null;

			debugSocket = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			if (outputEndPoint != null) {
				outputSocket = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			}
			
			ConnectWithNetworkOutputCallback c = new ConnectWithNetworkOutputCallback (ConnectWithNetworkOutput);
			return c.BeginInvoke (debugSocket, outputSocket, debugEndPoint, outputEndPoint, logWriter, callback, outputSocket ?? debugSocket);
		}

		public static IAsyncResult BeginConnect (IPEndPoint debugEndPoint, StreamReader console, AsyncCallback callback, TextWriter logWriter = null) {
			Socket debugSocket = null;
			debugSocket = new Socket (AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			
			ConnectWithConsoleCallback c = new ConnectWithConsoleCallback (ConnectWithConsole);
			return c.BeginInvoke (debugSocket, debugEndPoint, console, logWriter, callback, debugSocket);
		}

		public static VirtualMachine EndConnect (IAsyncResult asyncResult) {
			if (asyncResult == null)
				throw new ArgumentNullException ("asyncResult");

			if (!asyncResult.IsCompleted)
				asyncResult.AsyncWaitHandle.WaitOne ();

			AsyncResult result = (AsyncResult) asyncResult;
			ConnectWithNetworkOutputCallback cb = (ConnectWithNetworkOutputCallback) result.AsyncDelegate;
			return cb.EndInvoke(asyncResult);
		}

		public static void CancelConnection (IAsyncResult asyncResult)
		{
			((IDisposable) asyncResult.AsyncState).Dispose();
		}
	}
}
