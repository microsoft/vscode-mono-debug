/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using System;
using System.Threading.Tasks;
using Microsoft.CSharp.RuntimeBinder;

namespace VSCodeDebug
{
    public interface IDebugSession
	{
		DebugResponse Dispatch(string command, dynamic args);

		Task<DebugResponse> Initialize(dynamic arguments);
		Task<DebugResponse> Launch(dynamic arguments);
		Task<DebugResponse> Attach(dynamic arguments);
		Task<DebugResponse> Disconnect();

		Task<DebugResponse> SetBreakpoints(Source source, int[] lines);
		Task<DebugResponse> SetFunctionBreakpoints();
		Task<DebugResponse> SetExceptionBreakpoints(string[] filter);

		Task<DebugResponse> Continue(int threadId);
		Task<DebugResponse> Next(int threadId);
		Task<DebugResponse> StepIn(int threadId);
		Task<DebugResponse> StepOut(int threadId);
		Task<DebugResponse> Pause(int threadId);

		Task<DebugResponse> Threads();
		Task<DebugResponse> StackTrace(int threadId, int levels);
		Task<DebugResponse> Scopes(int frameId);
		Task<DebugResponse> Variables(int reference);
		Task<DebugResponse> Source(int sourceReference);

		Task<DebugResponse> Evaluate(string context, int frameId, string expression);
	}

	public abstract class DebugSession : IDebugSession
	{
		private bool _debuggerLinesStartAt1;
		private bool _debuggerPathsAreURI;
		private bool _clientLinesStartAt1 = true;
		private bool _clientPathsAreURI = true;


		public DebugSession(bool debuggerLinesStartAt1, bool debuggerPathsAreURI = false) {
			_debuggerLinesStartAt1 = debuggerLinesStartAt1;
			_debuggerPathsAreURI = debuggerPathsAreURI;
		}

		public virtual DebugResponse Dispatch(string command, dynamic args)
		{
			int thread;

			switch (command) {

			case "initialize":
				if (args.linesStartAt1 != null) {
					_clientLinesStartAt1 = (bool)args.linesStartAt1;
				}
				var pathFormat = (string)args.pathFormat;
				if (pathFormat != null) {
					switch (pathFormat) {
					case "uri":
						_clientPathsAreURI = true;
						break;
					case "path":
						_clientPathsAreURI = false;
						break;
					default:
						return new DebugResponse(1015, "initialize: bad value '{_format}' for pathFormat", new { _format = pathFormat });
					}
				}
				return Initialize(args).Result;

			case "launch":
				return Launch(args).Result;

			case "attach":
				return Attach(args).Result;

			case "disconnect":
				return Disconnect().Result;

			case "next":
				thread = GetInt(args, "threadId", 0);
				return Next(thread).Result;

			case "continue":
				thread = GetInt(args, "threadId", 0);
				return Continue(thread).Result;

			case "stepIn":
				thread = GetInt(args, "threadId", 0);
				return StepIn(thread).Result;

			case "stepOut":
				thread = GetInt(args, "threadId", 0);
				return StepOut(thread).Result;

			case "pause":
				thread = GetInt(args, "threadId", 0);
				return Pause(thread).Result;

			case "stackTrace":
				int levels = GetInt(args, "levels", 0);
				thread = GetInt(args, "threadId", 0);
				return StackTrace(thread, levels).Result;

			case "scopes":
				int frameId0 = GetInt(args, "frameId", 0);
				return Scopes(frameId0).Result;

			case "variables":
				int varRef = GetInt(args, "variablesReference", -1);
				if (varRef == -1) {
					return new DebugResponse(1009, "variables: property 'variablesReference' is missing");
				}
				return Variables(varRef).Result;

			case "source":
				int sourceRef = GetInt(args, "sourceReference", -1);
				if (sourceRef == -1) {
					return new DebugResponse(1010, "source: property 'sourceReference' is missing");
				}
				return Source(sourceRef).Result;

			case "threads":
				return Threads().Result;

			case "setBreakpoints":
				string path = null;
				string name = null;
				int reference = 0;
				int noOfSources = 0;

				dynamic source = args.source;
				if (source != null) {
					string p = (string)source.path;
					if (p != null && p.Trim().Length > 0) {
						path = p;
						noOfSources++;
					}
					try {
						reference = (int)source.reference;
						if (reference > 0) {
							noOfSources++;
						}
					}
					catch (RuntimeBinderException) {
						reference = 0;
					}
					string nm = (string)source.name;
					if (nm != null && nm.Trim().Length > 0) {
						name = nm;
						noOfSources++;
					}
				}
				if (noOfSources > 0) {
					var src2 = new Source(name, path, reference);
					var lines = args.lines.ToObject<int[]>();
					return SetBreakpoints(src2, lines).Result;
				}
				return new DebugResponse(1012, "setBreakpoints: property 'source' is empty or misformed");

			case "setFunctionBreakpoints":
				return SetFunctionBreakpoints().Result;

			case "setExceptionBreakpoints":
				string[] filters = null;
				if (args.filters != null) {
					filters = args.filters.ToObject<string[]>();
				}
				else {
					filters = new string[0];
				}
				return SetExceptionBreakpoints(filters).Result;

			case "evaluate":
				var context = GetString(args, "context");
				int frameId = GetInt(args, "frameId", -1);
				var expression = GetString(args, "expression");
				if (expression == null) {
					return new DebugResponse(1013, "evaluate: property 'expression' is missing, null, or empty");
				}
				return Evaluate(context, frameId, expression).Result;

			default:
				return new DebugResponse(1014, "unrecognized request: {_request}", new { _request = command });
			}
		}

		public virtual Task<DebugResponse> Initialize(dynamic args)
		{
			return Task.FromResult(new DebugResponse());
		}

		public abstract Task<DebugResponse> Launch(dynamic arguments);

		public virtual Task<DebugResponse> Attach(dynamic arguments)
		{
			return Task.FromResult(new DebugResponse(1016, "Attach not supported"));
		}

		public virtual Task<DebugResponse> Disconnect()
		{
			return Task.FromResult(new DebugResponse());
		}

		public virtual Task<DebugResponse> SetFunctionBreakpoints()
		{
			return Task.FromResult(new DebugResponse());
		}

		public virtual Task<DebugResponse> SetExceptionBreakpoints(string[] filter)
		{
			return Task.FromResult(new DebugResponse());
		}

		public abstract Task<DebugResponse> SetBreakpoints(Source source, int[] lines);

		public abstract Task<DebugResponse> Continue(int thread);

		public abstract Task<DebugResponse> Next(int thread);

		public virtual Task<DebugResponse> StepIn(int thread)
		{
			return Task.FromResult(new DebugResponse(1017, "StepIn not supported"));
		}

		public virtual Task<DebugResponse> StepOut(int thread)
		{
			return Task.FromResult(new DebugResponse(1018, "StepOut not supported"));
		}

		public virtual Task<DebugResponse> Pause(int thread)
		{
			return Task.FromResult(new DebugResponse(1019, "Pause not supported"));
		}

		public abstract Task<DebugResponse> StackTrace(int thread, int levels);

		public abstract Task<DebugResponse> Scopes(int frameId);

		public abstract Task<DebugResponse> Variables(int reference);

		public virtual Task<DebugResponse> Source(int sourceId)
		{
			return Task.FromResult(new DebugResponse(1020, "Source not supported"));
		}

		public virtual Task<DebugResponse> Threads()
		{
			return Task.FromResult(new DebugResponse(new ThreadsResponseBody()));
		}

		public virtual Task<DebugResponse> Evaluate(string context, int frameId, string expression)
		{
			return Task.FromResult(new DebugResponse(1021, "Evaluate not supported"));
		}

		// protected

		protected int ConvertDebuggerLineToClient(int line)
		{
			if (_debuggerLinesStartAt1) {
				return _clientLinesStartAt1 ? line : line - 1;
			}
			else {
				return _clientLinesStartAt1 ? line + 1 : line;
			}
		}

		protected int ConvertClientLineToDebugger(int line)
		{
			if (_debuggerLinesStartAt1) {
				return _clientLinesStartAt1 ? line : line + 1;
			}
			else {
				return _clientLinesStartAt1 ? line - 1 : line;
			}
		}

		protected int ConvertDebuggerColumnToClient(int column)
		{
			// TODO@AW same as line
			return column;
		}

		protected string ConvertDebuggerPathToClient(string path)
		{
			if (_debuggerPathsAreURI) {
				if (_clientPathsAreURI) {
					return path;
				}
				else {
					Uri uri = new Uri(path);
					return uri.LocalPath;
				}
			}
			else {
				if (_clientPathsAreURI) {
					try {
						var uri = new System.Uri(path);
						return uri.AbsoluteUri;
					}
					catch {
						return null;
					}
				}
				else {
					return path;
				}
			}
		}

		protected string ConvertClientPathToDebugger(string clientPath)
		{
			if (clientPath == null) {
				return null;
			}

			if (_debuggerPathsAreURI) {
				if (_clientPathsAreURI) {
					return clientPath;
				}
				else {
					var uri = new System.Uri(clientPath);
					return uri.AbsoluteUri;
				}
			}
			else {
				if (_clientPathsAreURI) {
					if (Uri.IsWellFormedUriString(clientPath, UriKind.Absolute)) {
						Uri uri = new Uri(clientPath);
						return uri.LocalPath;
					}
					Console.Error.WriteLine("path not well formed: '{0}'", clientPath);
					return null;
				}
				else {
					return clientPath;
				}
			}
		}

		// private

		private static bool GetBool(dynamic args, string property)
		{
			try {
				return (bool)args[property];
			}
			catch (RuntimeBinderException) {
			}
			return false;
		}

		private static int GetInt(dynamic args, string property, int dflt)
		{
			try {
				return (int)args[property];
			}
			catch (RuntimeBinderException) {
				// ignore and return default value
			}
			return dflt;
		}

		private static string GetString(dynamic args, string property, string dflt = null)
		{
			var s = (string)args[property];
			if (s == null) {
				return dflt;
			}
			s = s.Trim();
			if (s.Length == 0) {
				return dflt;
			}
			return s;
		}
	}
}
