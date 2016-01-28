/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace OpenDebug
{
	internal class Program
	{
		const int DEFAULT_PORT = 4711;

		private static bool trace_requests;
		private static bool trace_responses;

		private static void Main(string[] argv)
		{
			int port = -1;

			// parse command line arguments
			foreach (var a in argv) {
				switch (a) {
				case "--trace":
					trace_requests = true;
					break;
				case "--trace=response":
					trace_requests = true;
					trace_responses = true;
					break;
				case "--server":
					port = DEFAULT_PORT;
					break;
				default:
					if (a.StartsWith("--server=")) {
						if (!int.TryParse(a.Substring("--server=".Length), out port)) {
							port = DEFAULT_PORT;
						}
					}
					break;
				}
			}

			if (port > 0) {
				// TCP/IP server
				Console.Error.WriteLine("waiting for debug protocol on port " + port);
				RunServer(port);
			} else {
			// stdin/stdout
				Console.Error.WriteLine("waiting for debug protocol on stdin/stdout");
			Dispatch(Console.OpenStandardInput(), Console.OpenStandardOutput());
				System.Threading.Thread.Sleep(300);	// wait a bit on exit so that remaining output events can drain...
			}
		}

		private static void RunServer(int port)
		{
			TcpListener serverSocket = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
			serverSocket.Start();

			new System.Threading.Thread(() => {
				while (true) {
					var clientSocket = serverSocket.AcceptSocket();
					if (clientSocket != null) {
						Console.Error.WriteLine(">> accepted connection from client");

						new System.Threading.Thread(() => {
							using (var networkStream = new NetworkStream(clientSocket)) {
								try {
									Dispatch(networkStream, networkStream);
								}
								catch (Exception e) {
									Console.Error.WriteLine("Exception: " + e);
								}
							}
							#if DNXCORE50
							clientSocket.Dispose();
							#else
							clientSocket.Close();
							#endif
							Console.Error.WriteLine(">> client connection closed");
						}).Start();
					}
				}
			}).Start();
		}

		private static void Dispatch(Stream inputStream, Stream outputStream)
		{
			V8ServerProtocol protocol = new V8ServerProtocol(inputStream, outputStream);

			protocol.TRACE = trace_requests;
			protocol.TRACE_RESPONSE = trace_responses;

			IDebugSession debugSession = null;

			var r = protocol.Start((string command, dynamic args, IResponder responder) => {

				if (args == null) {
					args = new { };
				}

				if (command == "initialize") {
					string adapterID = Utilities.GetString(args, "adapterID");
					if (adapterID == null) {
						responder.SetBody(new ErrorResponseBody(new Message(1101, "initialize: property 'adapterID' is missing or empty")));
						return;
					}

					debugSession = EngineFactory.CreateDebugSession(adapterID, (e) => protocol.SendEvent(e.type, e));
					if (debugSession == null) {
						responder.SetBody(new ErrorResponseBody(new Message(1103, "initialize: can't create debug session for adapter '{_id}'", new { _id = adapterID })));
						return;
					}
				}

				if (debugSession != null) {

					try {
						DebugResult dr = debugSession.Dispatch(command, args);
						if (dr != null) {
							responder.SetBody(dr.Body);

							if (dr.Events != null) {
								foreach (var e in dr.Events) {
									responder.AddEvent(e.type, e);
								}
							}
						}
					}
					catch (Exception e) {
						responder.SetBody(new ErrorResponseBody(new Message(1104, "error while processing request '{_request}' (exception: {_exception})", new { _request = command, _exception = e.Message })));
					}

					if (command == "disconnect") {
						protocol.Stop();
					}
				}

			}).Result;
		}
	}
}
