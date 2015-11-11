/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;

namespace OpenDebug
{
	public class V8Message
	{
		public int seq;
		public string type;

		public V8Message(string typ) {
			type = typ;
		}
	}

	public class V8Request : V8Message
	{
		public string command;
		public dynamic arguments;

		public V8Request(int id, string cmd, dynamic arg) : base("request") {
			seq = id;
			command = cmd;
			arguments = arg;
		}
	}

	public class V8Response0 : V8Message
	{
		public bool success;
		public string message;
		public int request_seq;
		public string command;
		public dynamic body;

		public V8Response0() : base("response") {
		}

		public V8Response0(int rseq, string cmd) : base("response") {
			request_seq = rseq;
			command = cmd;
		}
	}

	public class V8Response : V8Response0
	{
		public bool running;
		public dynamic refs;

		public V8Response() {
		}

		public V8Response(string msg) {
			success = false;
			message = msg;
		}

		public V8Response(bool succ, string msg) {
			success = succ;
			message = msg;
		}

		public V8Response(dynamic m) {
			seq = m.seq;
			success = m.success;
			message = m.message;
			request_seq = m.request_seq;
			command = m.command;
			body = m.body;
			running = m.running;
			refs = m.refs;
		}
	}

	public class V8Event : V8Message
	{
		[JsonProperty(PropertyName = "event")]
		public string eventType;
		public dynamic body;

		public V8Event() : base("event") {
		}

		public V8Event(dynamic m) : base("event") {
			seq = m.seq;
			eventType = m["event"];
			body = m.body;
		}

		public V8Event(string type, dynamic bdy = null) : base("event") {
			eventType = type;
			body = bdy;
		}
	}

	//---------------------------------------------------------------------------------------------------

	/*
	 * V8Protocol is a base class which defines common constants and utilities for client and server
	 */
	public class V8Protocol
	{
		protected bool PRINT_NOISE = false;

		protected const int BUFFER_SIZE = 4096;
		protected const int RESPONSE_TIMEOUT = 5000;	// 5 seconds
		protected const string TWO_CRLF = "\r\n\r\n";
		protected static readonly Regex CONTENT_LENGTH_MATCHER = new Regex(@"Content-Length: (\d+)");
		protected static readonly Regex VERSION_MATCHER = new Regex(@"Embedding-Host:\snode\sv(\d+)\.\d+\.\d+");

		protected static readonly Encoding Encoding = System.Text.Encoding.UTF8;


		protected static byte[] ConvertToBytes(V8Message request)
		{
			var asJson = JsonConvert.SerializeObject(request);
			byte[] jsonBytes = Encoding.GetBytes(asJson);

			string header = string.Format("Content-Length: {0}{1}", jsonBytes.Length, TWO_CRLF);
			byte[] headerBytes = Encoding.GetBytes(header);

			byte[] data = new byte[headerBytes.Length + jsonBytes.Length];
			System.Buffer.BlockCopy(headerBytes, 0, data, 0, headerBytes.Length);
			System.Buffer.BlockCopy(jsonBytes, 0, data, headerBytes.Length, jsonBytes.Length);

			return data;
		}
	}

	/*
	 * The V8ClientProtocol can be used to talk to a server that implements the V8 protocol, e.g. node.js
	 */
	public class V8ClientProtocol : V8Protocol
	{
		private TcpClient _client;
		private Dictionary<int, TaskCompletionSource<V8Response>> _pendingRequests;

		private int _sequenceNumber;

		private ByteBuffer _rawData;
		private int _contentLength;
		private bool _connected;
		private bool _inTimeoutMode;

		public Action<V8Event> Callback;
		public int EmbeddedHostVersion;


		public V8ClientProtocol() {
			EmbeddedHostVersion = -1;
			_sequenceNumber = 1;
			_contentLength = -1;
			_rawData = new ByteBuffer();
			_client = new TcpClient();
			_pendingRequests = new Dictionary<int, TaskCompletionSource<V8Response>>();
		}

		public Task<V8Response> Connect(string address, int port)
		{
			var tcs = new TaskCompletionSource<V8Response>();
			_client.BeginConnect(address, port,
				result => {
					try {
						_client.EndConnect(result);
					}
					catch (Exception e) {
						tcs.SetResult(new V8Response(string.Format("error '{0}'", e.Message)));
						return;
					}

					_connected = true;

					byte[] buffer = new byte[BUFFER_SIZE];
					NetworkStream networkStream = _client.GetStream();
					networkStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, buffer);

					tcs.SetResult(new V8Response(true, "Connected"));
				}, null);
			return tcs.Task;
		}

		public void Shutdown()
		{
			_connected = false;
		}

		public Task<V8Response> Command(string command, dynamic args = null, int timeout = RESPONSE_TIMEOUT)
		{
			var tcs = new TaskCompletionSource<V8Response>();

			if (_connected) {

				if (_inTimeoutMode) {
					// Console.Error.WriteLine("request {0} in timeout mode", command);
					tcs.SetResult(new V8Response("cancelled because node is unresponsive"));
					return tcs.Task;
				}

				V8Request request = null;
				lock (_pendingRequests) {
					request = new V8Request(_sequenceNumber++, command, args);
					// wait for response
					_pendingRequests.Add(request.seq, tcs);
				}

				var ct = new CancellationTokenSource(timeout);
				ct.Token.Register(() => {
					lock (_pendingRequests) {
						if (_pendingRequests.ContainsKey(request.seq)) {
							_pendingRequests.Remove(request.seq);
							_inTimeoutMode = true;
							tcs.TrySetResult(new V8Response(string.Format("timeout after {0} ms", timeout)));
							Callback.Invoke(new V8Event("diagnostic", new { reason = "unresponsive" }));
						}
					}
				}, useSynchronizationContext: false);

				var data = ConvertToBytes(request);

				try {
					NetworkStream networkStream = _client.GetStream();
					networkStream.BeginWrite(data, 0, data.Length,
						result => {
							try {
								networkStream.EndWrite(result);
							}
							catch (Exception e) {
								tcs.SetException(e);
							}
						},
						null);
				}
				catch (Exception e) {
					//tcs.SetException(e);
					tcs.SetResult(new V8Response(string.Format("error '{0}'", e.Message)));
				}
			}
			else {
				tcs.SetResult(new V8Response("not connected"));
			}

			return tcs.Task;
		}

		// ---- private ------------------------------------------------------------------------

		private void ReadCallback(IAsyncResult result)
		{
			int read;
			NetworkStream networkStream;
			try {
				networkStream = _client.GetStream();
				read = networkStream.EndRead(result);
			}
			catch (Exception) {
				Callback.Invoke(new V8Event("terminated"));
				return;
			}

			if (read == 0) {
				_connected = false;
				Callback.Invoke(new V8Event("terminated"));    // The connection has been closed
				return;
			}

			byte[] buffer = result.AsyncState as byte[];
			_rawData.Append(buffer, read);
			ProcessData();

			try {
				networkStream.BeginRead(buffer, 0, buffer.Length, ReadCallback, buffer);
			}
			catch {
				//Callback.Invoke("error", null);     // An error has occured when reading
				Callback.Invoke(new V8Event("terminated"));     // An error has occured when reading
			}
		}

		private void ProcessData()
		{
			while (true) {
				if (_contentLength >= 0) {
					if (_rawData.Length >= _contentLength) {
						var buf = _rawData.RemoveFirst(_contentLength);

						_contentLength = -1;

						Dispatch(Encoding.GetString(buf));

						continue;   // there may be more complete messages to process
					}
				}
				else {
					string s = _rawData.GetString(Encoding);
					var idx = s.IndexOf(TWO_CRLF);
					if (idx != -1) {
						Match m = CONTENT_LENGTH_MATCHER.Match(s);
						if (m.Success && m.Groups.Count == 2) {
							_contentLength = Convert.ToInt32(m.Groups[1].ToString());

							var bb = _rawData.RemoveFirst(idx + TWO_CRLF.Length);
							if (bb.Length > 0 && bb[0] != 'C') {
								var ss = Encoding.GetString(bb).Trim();
								if (ss.Length > 0 && !ss.StartsWith("Content-Length:")) {
									Match vm = VERSION_MATCHER.Match(ss);
									if (vm.Success && vm.Groups.Count == 2) {
										EmbeddedHostVersion = Convert.ToInt32(vm.Groups[1].ToString());
									}
									if (PRINT_NOISE) Console.Error.WriteLine(ss);    // print any noise between messages
								}
							}

							continue;   // try to handle a complete message
						}
					}
				}
				break;
			}
		}

		private void Dispatch(string body)
		{
			var requestOrResponse = JsonConvert.DeserializeObject<dynamic>(body);
			if (requestOrResponse != null) {
				if (requestOrResponse.type == "response") {
					var response = new V8Response(requestOrResponse);
					//var response = JsonConvert.DeserializeObject<V8Response>(body);
					int seq = response.request_seq;
					lock (_pendingRequests) {
						if (_inTimeoutMode) {
							_inTimeoutMode = false;
							Callback.Invoke(new V8Event("diagnostic", new { reason = "responsive" }));
						}
						if (_pendingRequests.ContainsKey(seq)) {
							var tcs = _pendingRequests[seq];
							_pendingRequests.Remove(seq);
							tcs.SetResult(response);
						}
					}
				}
				else if (requestOrResponse.type == "event") {
					if (Callback != null) {
						var ev = new V8Event(requestOrResponse);
						//var ev = JsonConvert.DeserializeObject<V8Event>(body);
						Callback.Invoke(ev);
					}
				}
			}
		}
	}

	//---------------------------------------------------------------------------------------------------

	public interface IResponder
	{
		void SetBody(dynamic body);
		void AddEvent(string type, dynamic body);
	}

	/*
     * The V8ServerProtocol can be used to implement a server that uses the V8 protocol.
     */
	public class V8ServerProtocol : V8Protocol
	{
		public bool TRACE;
		public bool TRACE_RESPONSE;

		private int _sequenceNumber;

		private Stream _inputStream;
		private Stream _outputStream;

		private ByteBuffer _rawData;
		private int _bodyLength;

		private bool _stopRequested;

		private Action<string, dynamic, Responder> _callback;

		private Queue<V8Event> _queuedEvent;


		public V8ServerProtocol(Stream inputStream, Stream outputStream) {
			_sequenceNumber = 1;
			_inputStream = inputStream;
			_outputStream = outputStream;
			_bodyLength = -1;
			_rawData = new ByteBuffer();
			_queuedEvent = new Queue<V8Event>();
		}

		public async Task<int> Start(Action<string, dynamic, IResponder> cb)
		{
			_callback = cb;

			byte[] buffer = new byte[BUFFER_SIZE];

			_stopRequested = false;
			while (!_stopRequested) {
				var read = await _inputStream.ReadAsync(buffer, 0, buffer.Length);

				if (read == 0) {
					break;
				}

				if (read > 0) {
					_rawData.Append(buffer, read);
					ProcessData();
				}
			}
			return 0;
		}

		public void Stop()
		{
			_stopRequested = true;
		}

		public void SendEvent(string eventType, dynamic body)
		{
			SendMessage(new V8Event(eventType, body));
		}

		public void SendEventLater(string eventType, dynamic body)
		{
			_queuedEvent.Enqueue(new V8Event(eventType, body));
		}

		// ---- private ------------------------------------------------------------------------

		private void ProcessData()
		{
			while (true) {
				if (_bodyLength >= 0) {
					if (_rawData.Length >= _bodyLength) {
						var buf = _rawData.RemoveFirst(_bodyLength);

						_bodyLength = -1;

						Dispatch(Encoding.GetString(buf));

						continue;   // there may be more complete messages to process
					}
				}
				else {
					string s = _rawData.GetString(Encoding);
					var idx = s.IndexOf(TWO_CRLF);
					if (idx != -1) {
						Match m = CONTENT_LENGTH_MATCHER.Match(s);
						if (m.Success && m.Groups.Count == 2) {
							_bodyLength = Convert.ToInt32(m.Groups[1].ToString());

							_rawData.RemoveFirst(idx + TWO_CRLF.Length);

							continue;   // try to handle a complete message
						}
					}
				}
				break;
			}
		}

		private void Dispatch(string req)
		{
			var request = JsonConvert.DeserializeObject<V8Request>(req);
			if (request != null && request.type == "request") {
				if (TRACE)
					Console.Error.WriteLine(string.Format("C {0}: {1}", request.command, JsonConvert.SerializeObject(request.arguments)));

				if (_callback != null) {
					var response = new V8Response0(request.seq, request.command);
					var responder = new Responder(this, response);

					_callback.Invoke(request.command, request.arguments, responder);

					SendMessage(response);

					while (_queuedEvent.Count > 0) {
						var e = _queuedEvent.Dequeue();
						SendMessage(e);
					}
				}
			}
		}

		private void SendMessage(V8Message message)
		{
			message.seq = _sequenceNumber++;

			if (TRACE_RESPONSE && message.type == "response") {
				Console.Error.WriteLine(string.Format(" R: {0}", JsonConvert.SerializeObject(message)));
			}
			if (TRACE && message.type == "event") {
				V8Event e = (V8Event)message;
				Console.Error.WriteLine(string.Format("E {0}: {1}", e.eventType, JsonConvert.SerializeObject(e.body)));
			}

			var data = ConvertToBytes(message);
			try {
				_outputStream.Write(data, 0, data.Length);
				_outputStream.Flush();
			}
			catch (Exception) {
				//
			}
		}
	}

	//--------------------------------------------------------------------------------------

	class Responder : IResponder
	{
		V8ServerProtocol _protocol;
		V8Response0 _response;

		public Responder(V8ServerProtocol protocol, V8Response0 response) {
			_protocol = protocol;
			_response = response;
		}

		public void SetBody(dynamic body) {
			_response.body = body;
			if (body is ErrorResponseBody) {
				var e = (ErrorResponseBody) body;
				var message = e.error;
				_response.success = false;
				_response.message = Utilities.ExpandVariables(message.format, message.variables);
			} else {
				_response.success = true;
			}
		}

		public void AddEvent(string type, dynamic body)
		{
			_protocol.SendEventLater(type, body);
		}
	}

	class ByteBuffer
	{
		private byte[] _buffer;

		public ByteBuffer() {
			_buffer = new byte[0];
		}

		public int Length {
			get { return _buffer.Length; }
		}

		public string GetString(Encoding enc)
		{
			return enc.GetString(_buffer);
		}

		public void Append(byte[] b, int length)
		{
			byte[] newBuffer = new byte[_buffer.Length + length];
			System.Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _buffer.Length);
			System.Buffer.BlockCopy(b, 0, newBuffer, _buffer.Length, length);
			_buffer = newBuffer;
		}

		public byte[] RemoveFirst(int n)
		{
			byte[] b = new byte[n];
			System.Buffer.BlockCopy(_buffer, 0, b, 0, n);
			byte[] newBuffer = new byte[_buffer.Length - n];
			System.Buffer.BlockCopy(_buffer, n, newBuffer, 0, _buffer.Length - n);
			_buffer = newBuffer;
			return b;
		}
	}
}
