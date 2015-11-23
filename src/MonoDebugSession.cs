/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Linq;
using System.Net;
using Mono.Debugger.Client;
using Mono.Debugging.Client;
using System.Collections.Concurrent;


namespace OpenDebug
{
	public class SDBDebugSession : DebugSession, IDebugSession
	{
		private const string MONO = "mono";
		private readonly string[] MONO_EXTENSIONS = new String[] {
			".cs",
			".fs", ".fsi", ".ml", ".mli", ".fsx", ".fsscript"
		};
		private const int MAX_CHILDREN = 100;

		private Handles<ObjectValue[]> _variableHandles;
		private Handles<Mono.Debugging.Client.StackFrame> _frameHandles;
		private ObjectValue _exception;
		private Dictionary<int, Thread> _seenThreads = new Dictionary<int, Thread>();
		private bool _attachMode = false;


		public SDBDebugSession(Action<DebugEvent> callback) : base(true)
		{
			_variableHandles = new Handles<ObjectValue[]>();
			_frameHandles = new Handles<Mono.Debugging.Client.StackFrame>();
			_seenThreads = new Dictionary<int, Thread>();

			Configuration.Current.MaxConnectionAttempts = 10;
			Configuration.Current.ConnectionAttemptInterval = 500;

			Debugger.Callback = (type, sourceLocation, threadinfo) => {
				int tid;
				switch (type) {
				case "TargetStopped":
					Stopped();
					callback.Invoke(CreateStoppedEvent("step", sourceLocation, threadinfo));
					break;

				case "TargetHitBreakpoint":
					Stopped();
					callback.Invoke(CreateStoppedEvent("breakpoint", sourceLocation, threadinfo));
					break;

				case "TargetExceptionThrown":
				case "TargetUnhandledException":
					Stopped();
					ExceptionInfo ex = Debugger.ActiveException;
					if (ex != null) {
						_exception = ex.Instance;
					}
					callback.Invoke(CreateStoppedEvent("exception", sourceLocation, threadinfo, Debugger.ActiveException.Message));
					break;

				case "TargetExited":
					callback.Invoke(new TerminatedEvent());
					break;

				case "TargetThreadStarted":
					tid = (int)threadinfo.Id;
					lock (_seenThreads) {
						_seenThreads[tid] = new Thread(tid, threadinfo.Name);
					}
					callback.Invoke(new ThreadEvent("started", tid));
					break;

				case "TargetThreadStopped":
					tid = (int)threadinfo.Id;
					lock (_seenThreads) {
						_seenThreads.Remove(tid);
					}
					callback.Invoke(new ThreadEvent("exited", tid));
					break;

				default:
					callback.Invoke(new DebugEvent(type));
					break;
				}
			};

			// the Soft Debugger is ready to accept breakpoints immediately (so it doesn't have to wait until the target is known)
			callback.Invoke(new InitializedEvent());
		}

		public override Task<DebugResult> Launch(dynamic args)
		{
			_attachMode = false;

			// validate argument 'program'
			string programPath = getString(args, "program");
			if (programPath == null) {
				return Task.FromResult(new DebugResult(1001, "launch: property 'program' is missing or empty"));
			}
			programPath = ConvertClientPathToDebugger(programPath);
			if (!File.Exists(programPath) && !Directory.Exists(programPath)) {
				return Task.FromResult(new DebugResult(1002, "launch: program '{path}' does not exist", new { path = programPath }));
			}

			// validate argument 'args'
			string[] arguments = null;
			if (args.args != null) {
				arguments = args.args.ToObject<string[]>();
				if (arguments != null && arguments.Length == 0) {
					arguments = null;
				}
			}

			// validate argument 'cwd'
			var workingDirectory = (string)args.cwd;
			if (workingDirectory != null) {
				workingDirectory = workingDirectory.Trim();
				if (workingDirectory.Length == 0) {
					return Task.FromResult(new DebugResult(1003, "launch: property 'workingDirectory' is empty"));
				}
				workingDirectory = ConvertClientPathToDebugger(workingDirectory);
				if (!Directory.Exists(workingDirectory)) {
					return Task.FromResult(new DebugResult(1004, "launch: workingDirectory '{path}' does not exist", new { path = workingDirectory }));
				}
			}
			workingDirectory = null;	// TODO@AW Why?

			// validate argument 'runtimeExecutable'
			var runtimeExecutable = (string)args.runtimeExecutable;
			if (runtimeExecutable != null) {
				runtimeExecutable = runtimeExecutable.Trim();
				if (runtimeExecutable.Length == 0) {
					return Task.FromResult(new DebugResult(1005, "launch: property 'runtimeExecutable' is empty"));
				}
				runtimeExecutable = ConvertClientPathToDebugger(runtimeExecutable);
				if (!File.Exists(runtimeExecutable)) {
					return Task.FromResult(new DebugResult(1006, "launch: runtimeExecutable '{path}' does not exist", new { path = runtimeExecutable }));
				}
			}

			// validate argument 'runtimeArgs'
			string[] runtimeArguments = null;
			if (args.runtimeArgs != null) {
				runtimeArguments = args.runtimeArgs.ToObject<string[]>();
				if (runtimeArguments != null && runtimeArguments.Length == 0) {
					runtimeArguments = null;
				}
			}

			// validate argument 'env'
			Dictionary<string, string> env = null;
			var environmentVariables = args.env;
			if (environmentVariables != null) {
				env = new Dictionary<string, string>();
				foreach (var entry in environmentVariables) {
					env.Add((string)entry.Name, (string)entry.Value);
				}
				if (env.Count == 0) {
					env = null;
				}
			}

			if (Utilities.IsOSX() || Utilities.IsLinux()) {
				const string host = "127.0.0.1";
				int port = Utilities.FindFreePort(55555);

				string mono_path = runtimeExecutable;
				if (mono_path == null) {
					if (!Terminal.IsOnPath(MONO)) {
						return Task.FromResult(new DebugResult(3001, "launch: can't find runtime '{_runtime}' on PATH", new { _runtime = MONO }));
					}
					mono_path = MONO;     // try to find mono through PATH
				}

				var mono_args = new String[runtimeArguments != null ? runtimeArguments.Length + 2 : 2];
				mono_args[0] = "--debug";
				mono_args[1] = String.Format("--debugger-agent=transport=dt_socket,server=y,address={0}:{1}", host, port);
				if (runtimeArguments != null) {
					runtimeArguments.CopyTo(mono_args, 2);
				}

				string program;
				if (workingDirectory == null) {
					// if no working dir given, we use the direct folder of the executable
					workingDirectory = Path.GetDirectoryName(programPath);
					program = Path.GetFileName(programPath);
				}
				else {
					// if working dir is given and if the executable is within that folder, we make the executable path relative to the working dir
					//program = Utilities.MakeRelativePath(workingDirectory, programPath);		// TODO@AW
					program = programPath;
				}

				var result = Terminal.LaunchInTerminal(workingDirectory, mono_path, mono_args, program, arguments, environmentVariables);
				if (!result.Success) {
					return Task.FromResult(new DebugResult(3002, "launch: can't launch terminal ({reason})", new { reason = result.Message }));
				}

				Debugger.Connect(IPAddress.Parse(host), port);
			}
			else {	// Generic & Windows
				CommandLine.WaitForSuspend();

				if (workingDirectory == null) {
					// if no working dir given, we use the direct folder of the executable
					workingDirectory = Path.GetDirectoryName(programPath);
				}
				Debugger.WorkingDirectory = workingDirectory;

				if (arguments != null) {
					string pargs = "";
					foreach (var a in arguments) {
						if (args.Length > 0) {
							pargs += ' ';
						}
						pargs += Terminal.Quote(a);
					}
					Debugger.Arguments = pargs;
				}

				if (environmentVariables != null) {
					var dict = Debugger.EnvironmentVariables;
					foreach (var entry in environmentVariables) {
						dict.Add(entry.Key, entry.Value);
					}
				}

				// TODO@AW we should use the runtimeExecutable
				// TODO@AW we should pass runtimeArgs

				var file = new FileInfo(programPath);
				Debugger.Run(file);
				// TODO@AW in case of errors?
			}

			return Task.FromResult(new DebugResult());
		}

		public override Task<DebugResult> Attach(dynamic args)
		{
			_attachMode = true;

			// validate argument 'address'
			var host = getString(args, "address");
			if (host == null) {
				return Task.FromResult(new DebugResult(1007, "attach: property 'address' is missing or empty"));
			}

			// validate argument 'port'
			var port = getInt(args, "port", -1);
			if (port == -1) {
				return Task.FromResult(new DebugResult(1008, "attach: property 'port' is missing"));
			}

			IPAddress address = Utilities.ResolveIPAddress(host);
			if (address == null) {
				return Task.FromResult(new DebugResult(3003, "attach: invalid address '{address}'", new { address = address }));
			}
			Debugger.Connect(address, port);
			return Task.FromResult(new DebugResult());
		}

		public override Task<DebugResult> Disconnect()
		{
			//CommandLine.WaitForSuspend();

			if (_attachMode) {
				Debugger.Disconnect();
			} else {
				// Let's not leave dead Mono processes behind...
				Debugger.Pause();
				Debugger.Kill();

				while (!Debugger.DebuggeeKilled) {
					System.Threading.Thread.Sleep(10);
				}
			}

			return Task.FromResult(new DebugResult());
		}

		public override Task<DebugResult> Continue(int thread)
		{
			CommandLine.WaitForSuspend();
			Debugger.Continue();
			return Task.FromResult(new DebugResult());
		}

		public override Task<DebugResult> Next(int thread)
		{
			CommandLine.WaitForSuspend();
			Debugger.StepOverLine();
			return Task.FromResult(new DebugResult());
		}

		public override Task<DebugResult> StepIn(int thread)
		{
			CommandLine.WaitForSuspend();
			Debugger.StepIntoLine();
			return Task.FromResult(new DebugResult());
		}

		public override Task<DebugResult> StepOut(int thread)
		{
			CommandLine.WaitForSuspend();
			Debugger.StepOutOfMethod();
			return Task.FromResult(new DebugResult());
		}

		public override Task<DebugResult> Pause(int thread)
		{
			Debugger.Pause();
			return Task.FromResult(new DebugResult());
		}

		public override Task<DebugResult> SetExceptionBreakpoints(string[] filter)
		{
			//CommandLine.WaitForSuspend();
			return Task.FromResult(new DebugResult());
		}

		public override Task<DebugResult> SetBreakpoints(Source source, int[] clientLines)
		{
			if (source.path == null) {
				// we do not support special sources
				return Task.FromResult(new DebugResult(new SetBreakpointsResponseBody()));
			}

			string path = ConvertClientPathToDebugger(source.path);

			if (!HasMonoExtension(path)) {
				// we only support breakpoints in files mono can handle
				return Task.FromResult(new DebugResult(new SetBreakpointsResponseBody()));
			}

			//CommandLine.WaitForSuspend();

			HashSet<int> lin = new HashSet<int>();
			for (int i = 0; i < clientLines.Length; i++) {
				lin.Add(ConvertClientLineToDebugger(clientLines[i]));
			}

			// find all breakpoints for the given path and remember their id and line number
			var bpts = new List<Tuple<int, int>>();
			foreach (var be in Debugger.Breakpoints) {
				var bp = be.Value as Mono.Debugging.Client.Breakpoint;
				if (bp != null && bp.FileName == path) {
					bpts.Add(new Tuple<int,int>((int)be.Key, (int)bp.Line));
				}
			}

			HashSet<int> lin2 = new HashSet<int>();
			foreach (var bpt in bpts) {
				if (lin.Contains(bpt.Item2)) {
					lin2.Add(bpt.Item2);
				}
				else {
					// Console.WriteLine("cleared bpt #{0} for line {1}", bpt.Item1, bpt.Item2);

					BreakEvent b;
					if (Debugger.Breakpoints.TryGetValue(bpt.Item1, out b)) {
						Debugger.Breakpoints.Remove(bpt.Item1);
						Debugger.BreakEvents.Remove(b);
					}
				}
			}

			for (int i = 0; i < clientLines.Length; i++) {
				var l = ConvertClientLineToDebugger(clientLines[i]);
				if (!lin2.Contains(l)) {
					var id = Debugger.GetBreakpointId();
					Debugger.Breakpoints.Add(id, Debugger.BreakEvents.Add(path, l));
					// Console.WriteLine("added bpt #{0} for line {1}", id, l);
				}
			}

			var breakpoints = new List<Breakpoint>();
			foreach (var l in clientLines) {
				breakpoints.Add(new Breakpoint(true, l));
			}
			return Task.FromResult(new DebugResult(new SetBreakpointsResponseBody(breakpoints)));
		}

		public override Task<DebugResult> StackTrace(int threadReference, int maxLevels)
		{
			CommandLine.WaitForSuspend();
			var stackFrames = new List<StackFrame>();

			ThreadInfo thread = Debugger.ActiveThread;
			if (thread.Id != threadReference) {
				Console.Error.WriteLine("stackTrace: unexpected: active thread should be the one requested");
				thread = FindThread(threadReference);
				if (thread != null) {
					thread.SetActive();
				}
			}

			var bt = thread.Backtrace;
			if (bt != null && bt.FrameCount >= 0) {
				for (var i = 0; i < bt.FrameCount; i++) {

					var frame = bt.GetFrame(i);
					var frameHandle = _frameHandles.Create(frame);

					string name = frame.SourceLocation.MethodName;
					string path = frame.SourceLocation.FileName;
					int line = frame.SourceLocation.Line;
					string sourceName = Path.GetFileName(path);

					var source = new Source(sourceName, ConvertDebuggerPathToClient(path));
					stackFrames.Add(new StackFrame(frameHandle, name, source, ConvertDebuggerLineToClient(line), 0));
				}
			}

			return Task.FromResult(new DebugResult(new StackTraceResponseBody(stackFrames)));
		}

		public override Task<DebugResult> Scopes(int frameId) {

			var scopes = new List<Scope>();

			var frame = _frameHandles.Get(frameId, null);

			if (frame.Index == 0 && _exception != null) {
				scopes.Add(new Scope("Exception", _variableHandles.Create(new ObjectValue[] { _exception })));
			}

			var parameters = new[] { frame.GetThisReference() }.Concat(frame.GetParameters()).Where(x => x != null);
			if (parameters.Any()) {
				scopes.Add(new Scope("Argument", _variableHandles.Create(parameters.ToArray())));
			}

			var locals = frame.GetLocalVariables();
			if (locals.Length > 0) {
				scopes.Add(new Scope("Local", _variableHandles.Create(locals)));
			}

			return Task.FromResult(new DebugResult(new ScopesResponseBody(scopes)));
		}

		public override Task<DebugResult> Variables(int reference)
		{
			CommandLine.WaitForSuspend();
			var variables = new List<Variable>();

			ObjectValue[] children;
			if (_variableHandles.TryGet(reference, out children)) {
				if (children != null && children.Length > 0) {

					bool more = false;
					if (children.Length > MAX_CHILDREN) {
						children = children.Take(MAX_CHILDREN).ToArray();
						more = true;
					}

					if (children.Length < 20) {
						// Wait for all values at once.
						WaitHandle.WaitAll(children.Select(x => x.WaitHandle).ToArray());
						foreach (var v in children) {
							variables.Add(CreateVariable(v));
						}
					}
					else {
						foreach (var v in children) {
							v.WaitHandle.WaitOne();
							variables.Add(CreateVariable(v));
						}
					}

					if (more) {
						variables.Add(new Variable("...", null));
					}
				}
			}

			return Task.FromResult(new DebugResult(new VariablesResponseBody(variables)));
		}

		public override Task<DebugResult> Threads()
		{
			var threads = new List<Thread>();
			var process = Debugger.ActiveProcess;
			if (process != null) {
				Dictionary<int, Thread> d;
				lock (_seenThreads) {
					d = new Dictionary<int, Thread>(_seenThreads);
				}
				foreach (var t in process.GetThreads()) {
					int tid = (int)t.Id;
					d[tid] = new Thread(tid, t.Name);
				}
				threads = d.Values.ToList();
			}
			return Task.FromResult(new DebugResult(new ThreadsResponseBody(threads)));
		}

		public override Task<DebugResult> Evaluate(string context, int frameId, string expression)
		{
			string error = null;

			var frame = _frameHandles.Get(frameId, null);
			if (frame != null) {
				if (frame.ValidateExpression(expression)) {
					var val = frame.GetExpressionValue(expression, Debugger.Options.EvaluationOptions);
					val.WaitHandle.WaitOne();

					var flags = val.Flags;
					if (flags.HasFlag(ObjectValueFlags.Error) || flags.HasFlag(ObjectValueFlags.NotSupported)) {
						error = val.DisplayValue;
						if (error.IndexOf("reference not available in the current evaluation context") > 0) {
							error = "not available";
						}
					}
					else if (flags.HasFlag(ObjectValueFlags.Unknown)) {
						error = "invalid expression";
					}
					else if (flags.HasFlag(ObjectValueFlags.Object) && flags.HasFlag(ObjectValueFlags.Namespace)) {
						error = "not available";
					}
					else {
						int handle = 0;
						if (val.HasChildren) {
							handle = _variableHandles.Create(val.GetAllChildren());
						}
						return Task.FromResult(new DebugResult(new EvaluateResponseBody(val.DisplayValue, handle)));
					}
				}
				else {
					error = "invalid expression";
				}
			}
			else {
				error = "no active stackframe";
			}
			return Task.FromResult(new DebugResult(3004, "evaluate request failed ({_reason})", new { _reason = error } ));
		}

		//---- private ------------------------------------------

		private StoppedEvent CreateStoppedEvent(string reason, SourceLocation sl, ThreadInfo ti, string text = null)
		{
			return new StoppedEvent(reason, new Source(ConvertDebuggerPathToClient(sl.FileName)), ConvertDebuggerLineToClient(sl.Line), sl.Column, text, (int)ti.Id);
		}

		private ThreadInfo FindThread(int threadReference)
		{
			var process = Debugger.ActiveProcess;
			if (process != null) {
				foreach (var t in process.GetThreads()) {
					if (t.Id == threadReference) {
						return t;
					}
				}
			}
			return null;
		}

		private void Stopped()
		{
			_exception = null;
			_variableHandles.Reset();
			_frameHandles.Reset();
		}

		private Variable CreateVariable(ObjectValue v)
		{
			var pname = String.Format("{0} {1}", v.TypeName, v.Name);
			return new Variable(pname, v.DisplayValue, v.HasChildren ? _variableHandles.Create(v.GetAllChildren()) : 0);
		}

		private bool HasMonoExtension(string path)
		{
			foreach (var e in MONO_EXTENSIONS) {
				if (path.EndsWith(e)) {
					return true;
				}
			}
			return false;
		}

		protected static int getInt(dynamic container, string propertyName, int dflt = 0)
		{
			try {
				return (int)container[propertyName];
			}
			catch (Exception) {
				// ignore and return default value
			}
			return dflt;
		}

		protected static string getString(dynamic args, string property, string dflt = null)
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
