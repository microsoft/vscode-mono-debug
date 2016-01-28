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

	public class V8Response : V8Message
	{
		public bool success;
		public string message;
		public int request_seq;
		public string command;
		public dynamic body;

		public V8Response() : base("response") {
		}

		public V8Response(int rseq, string cmd) : base("response") {
			request_seq = rseq;
			command = cmd;
		}

		public void SetBody(dynamic bdy) {
			body = bdy;
			if (bdy is ErrorResponseBody) {
				var e = (ErrorResponseBody) bdy;
				var msg = e.error;
				success = false;
				message = Utilities.ExpandVariables(msg.format, msg.variables);
			} else {
				success = true;
			}
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

	/*
     * The V8ServerProtocol can be used to implement a server that uses the V8 protocol.
     */
	public class V8ServerProtocol
	{
		public bool TRACE;
		public bool TRACE_RESPONSE;

		protected const int BUFFER_SIZE = 4096;
		protected const string TWO_CRLF = "\r\n\r\n";
		protected static readonly Regex CONTENT_LENGTH_MATCHER = new Regex(@"Content-Length: (\d+)");

		protected static readonly Encoding Encoding = System.Text.Encoding.UTF8;

		private int _sequenceNumber;

		private Stream _inputStream;
		private Stream _outputStream;

		private ByteBuffer _rawData;
		private int _bodyLength;

		private bool _stopRequested;

		private Action<string, dynamic, V8Response> _callback;


		public V8ServerProtocol(Stream inputStream, Stream outputStream) {
			_sequenceNumber = 1;
			_inputStream = inputStream;
			_outputStream = outputStream;
			_bodyLength = -1;
			_rawData = new ByteBuffer();
		}

		public async Task<int> Start(Action<string, dynamic, V8Response> cb)
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
					var response = new V8Response(request.seq, request.command);

					_callback.Invoke(request.command, request.arguments, response);

					SendMessage(response);
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

		private static byte[] ConvertToBytes(V8Message request)
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

	//--------------------------------------------------------------------------------------

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
