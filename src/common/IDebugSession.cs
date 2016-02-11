/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using System;
using System.Threading.Tasks;
using Microsoft.CSharp.RuntimeBinder;

namespace OpenDebug
{
    public interface IDebugSession
	{
		DebugResult Dispatch(string command, dynamic args);

		Task<DebugResult> Initialize(dynamic arguments);
		Task<DebugResult> Launch(dynamic arguments);
		Task<DebugResult> Attach(dynamic arguments);
		Task<DebugResult> Disconnect();

		Task<DebugResult> SetBreakpoints(Source source, int[] lines);
		Task<DebugResult> SetFunctionBreakpoints();
		Task<DebugResult> SetExceptionBreakpoints(string[] filter);

		Task<DebugResult> Continue(int threadId);
		Task<DebugResult> Next(int threadId);
		Task<DebugResult> StepIn(int threadId);
		Task<DebugResult> StepOut(int threadId);
		Task<DebugResult> Pause(int threadId);

		Task<DebugResult> Threads();
		Task<DebugResult> StackTrace(int threadId, int levels);
		Task<DebugResult> Scopes(int frameId);
		Task<DebugResult> Variables(int reference);
		Task<DebugResult> Source(int sourceReference);

		Task<DebugResult> Evaluate(string context, int frameId, string expression);
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

		public virtual DebugResult Dispatch(string command, dynamic args)
		{
			int thread;

			switch (command) {

			case "initialize":
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
					return new DebugResult(1009, "variables: property 'variablesReference' is missing");
				}
				return Variables(varRef).Result;

			case "source":
				int sourceRef = GetInt(args, "sourceReference", -1);
				if (sourceRef == -1) {
					return new DebugResult(1010, "source: property 'sourceReference' is missing");
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
				return new DebugResult(1012, "setBreakpoints: property 'source' is empty or misformed");

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
					return new DebugResult(1013, "evaluate: property 'expression' is missing, null, or empty");
				}
				return Evaluate(context, frameId, expression).Result;

			default:
				return new DebugResult(1014, "unrecognized request: {_request}", new { _request = command });
			}
		}

		public virtual Task<DebugResult> Initialize(dynamic args)
		{
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
					return Task.FromResult(new DebugResult(1015, "initialize: bad value '{_format}' for pathFormat", new { _format = pathFormat }));
				}
			}

			return Task.FromResult(new DebugResult());
		}

		public abstract Task<DebugResult> Launch(dynamic arguments);

		public virtual Task<DebugResult> Attach(dynamic arguments)
		{
			return Task.FromResult(new DebugResult(1016, "Attach not supported"));
		}

		public virtual Task<DebugResult> Disconnect()
		{
			return Task.FromResult(new DebugResult());
		}

		public virtual Task<DebugResult> SetFunctionBreakpoints()
		{
			return Task.FromResult(new DebugResult());
		}

		public virtual Task<DebugResult> SetExceptionBreakpoints(string[] filter)
		{
			return Task.FromResult(new DebugResult());
		}

		public abstract Task<DebugResult> SetBreakpoints(Source source, int[] lines);

		public abstract Task<DebugResult> Continue(int thread);

		public abstract Task<DebugResult> Next(int thread);

		public virtual Task<DebugResult> StepIn(int thread)
		{
			return Task.FromResult(new DebugResult(1017, "StepIn not supported"));
		}

		public virtual Task<DebugResult> StepOut(int thread)
		{
			return Task.FromResult(new DebugResult(1018, "StepOut not supported"));
		}

		public virtual Task<DebugResult> Pause(int thread)
		{
			return Task.FromResult(new DebugResult(1019, "Pause not supported"));
		}

		public abstract Task<DebugResult> StackTrace(int thread, int levels);

		public abstract Task<DebugResult> Scopes(int frameId);

		public abstract Task<DebugResult> Variables(int reference);

		public virtual Task<DebugResult> Source(int sourceId)
		{
			return Task.FromResult(new DebugResult(1020, "Source not supported"));
		}

		public virtual Task<DebugResult> Threads()
		{
			return Task.FromResult(new DebugResult(new ThreadsResponseBody()));
		}

		public virtual Task<DebugResult> Evaluate(string context, int frameId, string expression)
		{
			return Task.FromResult(new DebugResult(1021, "Evaluate not supported"));
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
