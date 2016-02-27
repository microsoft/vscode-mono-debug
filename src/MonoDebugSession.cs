/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Linq;
using System.Net;
using Mono.Debugger.Client;
using Mono.Debugging.Client;
using Microsoft.CSharp.RuntimeBinder;


namespace VSCodeDebug
{
	public class MonoDebugSession : DebugSession
	{
		private const string MONO = "mono";
		private readonly string[] MONO_EXTENSIONS = new String[] {
			".cs",
			".fs", ".fsi", ".ml", ".mli", ".fsx", ".fsscript"
		};
		private const int MAX_CHILDREN = 100;

		private System.Diagnostics.Process _process;
		private Handles<ObjectValue[]> _variableHandles;
		private Handles<Mono.Debugging.Client.StackFrame> _frameHandles;
		private ObjectValue _exception;
		private Dictionary<int, Thread> _seenThreads = new Dictionary<int, Thread>();
		private bool _attachMode = false;
		private bool _terminated = false;
		private bool _stderrEOF = true;
		private bool _stdoutEOF = true;


		public MonoDebugSession() : base(true)
		{
			_variableHandles = new Handles<ObjectValue[]>();
			_frameHandles = new Handles<Mono.Debugging.Client.StackFrame>();
			_seenThreads = new Dictionary<int, Thread>();

			Configuration.Current.MaxConnectionAttempts = 10;
			Configuration.Current.ConnectionAttemptInterval = 500;

			// install an event handler in SDB
			Debugger.Callback = (type, threadinfo, text) => {
				int tid;
				switch (type) {
				case "TargetStopped":
					Stopped();
					SendEvent(CreateStoppedEvent("step", threadinfo));
					break;

				case "TargetHitBreakpoint":
					Stopped();
					SendEvent(CreateStoppedEvent("breakpoint", threadinfo));
					break;

				case "TargetExceptionThrown":
				case "TargetUnhandledException":
					Stopped();
					ExceptionInfo ex = Debugger.ActiveException;
					if (ex != null) {
						_exception = ex.Instance;
					}
					SendEvent(CreateStoppedEvent("exception", threadinfo, Debugger.ActiveException.Message));
					break;

				case "TargetExited":
					Terminate("target exited");
					break;

				case "TargetThreadStarted":
					tid = (int)threadinfo.Id;
					lock (_seenThreads) {
						_seenThreads[tid] = new Thread(tid, threadinfo.Name);
					}
					SendEvent(new ThreadEvent("started", tid));
					break;

				case "TargetThreadStopped":
					tid = (int)threadinfo.Id;
					lock (_seenThreads) {
						_seenThreads.Remove(tid);
					}
					SendEvent(new ThreadEvent("exited", tid));
					break;

				case "Output":
					SendOutput("stdout", text);
					break;

				case "ErrorOutput":
					SendOutput("stderr", text);
					break;

				default:
					SendEvent(new Event(type));
					break;
				}
			};

		}

		public override void Initialize(Response response, dynamic args)
		{
			OperatingSystem os = Environment.OSVersion;
			if (os.Platform != PlatformID.MacOSX && os.Platform != PlatformID.Unix) {
				SendErrorResponse(response, 3000, "Mono Debug is not supported on this platform ({_platform}).", new { _platform = os.Platform.ToString() }, true, true);
				return;
			}

			SendResponse(response, new Capabilities() {
				// This debug adapter does not need the configurationDoneRequest.
				supportsConfigurationDoneRequest = false,

				// This debug adapter does not support function breakpoints.
				supportsFunctionBreakpoints = false,

				// This debug adapter doesn't support conditional breakpoints.
				supportsConditionalBreakpoints = false,

				// This debug adapter does not support a side effect free evaluate request for data hovers.
				supportsEvaluateForHovers = false,

				// This debug adapter does not support exception breakpoint filters
				exceptionBreakpointFilters = new dynamic[0]
			});

			// Mono Debug is ready to accept breakpoints immediately
			SendEvent(new InitializedEvent());
		}

		public override void Launch(Response response, dynamic args)
		{
			_attachMode = false;

			// validate argument 'program'
			string programPath = getString(args, "program");
			if (programPath == null) {
				SendErrorResponse(response, 3001, "Property 'program' is missing or empty.", null);
				return;
			}
			programPath = ConvertClientPathToDebugger(programPath);
			if (!File.Exists(programPath) && !Directory.Exists(programPath)) {
				SendErrorResponse(response, 3002, "Program '{path}' does not exist.", new { path = programPath });
				return;
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
					SendErrorResponse(response, 3003, "Property 'cwd' is empty.");
					return;
				}
				workingDirectory = ConvertClientPathToDebugger(workingDirectory);
				if (!Directory.Exists(workingDirectory)) {
					SendErrorResponse(response, 3004, "Working directory '{path}' does not exist.", new { path = workingDirectory });
					return;
				}
			}

			// validate argument 'runtimeExecutable'
			var runtimeExecutable = (string)args.runtimeExecutable;
			if (runtimeExecutable != null) {
				runtimeExecutable = runtimeExecutable.Trim();
				if (runtimeExecutable.Length == 0) {
					SendErrorResponse(response, 3005, "Property 'runtimeExecutable' is empty.");
					return;
				}
				runtimeExecutable = ConvertClientPathToDebugger(runtimeExecutable);
				if (!File.Exists(runtimeExecutable)) {
					SendErrorResponse(response, 3006, "Runtime executable '{path}' does not exist.", new { path = runtimeExecutable });
					return;
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
						SendErrorResponse(response, 3011, "Can't find runtime '{_runtime}' on PATH.", new { _runtime = MONO });
						return;
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
					// if working dir is given and if the executable is within that folder, we make the program path relative to the working dir
					program = Utilities.MakeRelativePath(workingDirectory, programPath);
				}

				bool externalConsole = getBool(args, "externalConsole", false);
				if (externalConsole) {
					var result = Terminal.LaunchInTerminal(workingDirectory, mono_path, mono_args, program, arguments, env);
					if (!result.Success) {
						SendErrorResponse(response, 3012, "Can't launch terminal ({reason}).", new { reason = result.Message });
						return;
					}
				} else {

					_process = new System.Diagnostics.Process();
					_process.StartInfo.CreateNoWindow = true;
					_process.StartInfo.UseShellExecute = false;
					_process.StartInfo.WorkingDirectory = workingDirectory;
					_process.StartInfo.FileName = mono_path;
					_process.StartInfo.Arguments = string.Format("{0} {1} {2}", Terminal.ConcatArgs(mono_args), Terminal.Quote(program), Terminal.ConcatArgs(arguments));

					_stdoutEOF = false;
					_process.StartInfo.RedirectStandardOutput = true;
					_process.OutputDataReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) => {
						if (e.Data == null) {
							_stdoutEOF = true;
						}
						SendOutput("stdout", e.Data);
					};

					_stderrEOF = false;
					_process.StartInfo.RedirectStandardError = true;
					_process.ErrorDataReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) => {
						if (e.Data == null) {
							_stderrEOF = true;
						}
						SendOutput("stderr", e.Data);
					};

					_process.EnableRaisingEvents = true;
					_process.Exited += (object sender, EventArgs e) => {
						Terminate("node process exited");
					};

					if (env != null) {
						// we cannot set the env vars on the process StartInfo because we need to set StartInfo.UseShellExecute to true at the same time.
						// instead we set the env vars on MonoDebug itself because we know that MonoDebug lives as long as a debug session.
						foreach (var entry in env) {
							System.Environment.SetEnvironmentVariable(entry.Key, entry.Value);
						}
					}

					var cmd = string.Format("{0} {1}", mono_path, _process.StartInfo.Arguments);
					SendOutput("console", cmd);

					try {
						_process.Start();
						_process.BeginOutputReadLine();
						_process.BeginErrorReadLine();
					}
					catch (Exception e) {
						SendErrorResponse(response, 3012, "Can't launch terminal ({reason}).", new { reason = e.Message });
						return;
					}
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

			SendResponse(response);
		}

		public override void Attach(Response response, dynamic args)
		{
			_attachMode = true;

			// validate argument 'address'
			var host = getString(args, "address");
			if (host == null) {
				SendErrorResponse(response, 3007, "Property 'address' is missing or empty.");
				return;
			}

			// validate argument 'port'
			var port = getInt(args, "port", -1);
			if (port == -1) {
				SendErrorResponse(response, 3008, "Property 'port' is missing.");
				return;
			}

			IPAddress address = Utilities.ResolveIPAddress(host);
			if (address == null) {
				SendErrorResponse(response, 3013, "Invalid address '{address}'.", new { address = address });
				return;
			}
			Debugger.Connect(address, port);

			SendResponse(response);
		}

		public override void Disconnect(Response response, dynamic args)
		{
			if (_attachMode) {
				Debugger.Disconnect();
			} else {
				// Let's not leave dead Mono processes behind...
				if (_process != null) {
					_process.Kill();
					_process = null;
				} else {
					Debugger.Pause();
					Debugger.Kill();

					while (!Debugger.DebuggeeKilled) {
						System.Threading.Thread.Sleep(10);
					}
				}
			}

			SendResponse(response);
		}

		public override void Continue(Response response, dynamic args)
		{
			CommandLine.WaitForSuspend();
			Debugger.Continue();
			SendResponse(response);
		}

		public override void Next(Response response, dynamic args)
		{
			CommandLine.WaitForSuspend();
			Debugger.StepOverLine();
			SendResponse(response);
		}

		public override void StepIn(Response response, dynamic args)
		{
			CommandLine.WaitForSuspend();
			Debugger.StepIntoLine();
			SendResponse(response);
		}

		public override void StepOut(Response response, dynamic args)
		{
			CommandLine.WaitForSuspend();
			Debugger.StepOutOfMethod();
			SendResponse(response);
		}

		public override void Pause(Response response, dynamic args)
		{
			Debugger.Pause();
			SendResponse(response);
		}

		public override void SetBreakpoints(Response response, dynamic args)
		{
			string path = null;
			if (args.source != null) {
				string p = (string)args.source.path;
				if (p != null && p.Trim().Length > 0) {
					path = p;
				}
			}
			if (path == null) {
				SendErrorResponse(response, 3010, "setBreakpoints: property 'source' is empty or misformed", null, false, true);
				return;
			}
			path = ConvertClientPathToDebugger(path);

			if (!HasMonoExtension(path)) {
				// we only support breakpoints in files mono can handle
				SendResponse(response, new SetBreakpointsResponseBody());
				return;
			}

			var clientLines = args.lines.ToObject<int[]>();
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

			response.SetBody(new SetBreakpointsResponseBody(breakpoints));
		}

		public override void StackTrace(Response response, dynamic args)
		{
			int maxLevels = getInt(args, "levels", 10);
			int threadReference = getInt(args, "threadId", 0);

			CommandLine.WaitForSuspend();
			var stackFrames = new List<StackFrame>();

			ThreadInfo thread = Debugger.ActiveThread;
			if (thread.Id != threadReference) {
				// Console.Error.WriteLine("stackTrace: unexpected: active thread should be the one requested");
				thread = FindThread(threadReference);
				if (thread != null) {
					thread.SetActive();
				}
			}

			var bt = thread.Backtrace;
			if (bt != null && bt.FrameCount >= 0) {
				for (var i = 0; i < Math.Min(bt.FrameCount, maxLevels); i++) {

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

			SendResponse(response, new StackTraceResponseBody(stackFrames));
		}

		public override void Scopes(Response response, dynamic args) {

			int frameId = getInt(args, "frameId", 0);
			var frame = _frameHandles.Get(frameId, null);

			var scopes = new List<Scope>();

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

			SendResponse(response, new ScopesResponseBody(scopes));
		}

		public override void Variables(Response response, dynamic args)
		{
			int reference = getInt(args, "variablesReference", -1);
			if (reference == -1) {
				SendErrorResponse(response, 3009, "variables: property 'variablesReference' is missing", null, false, true);
				return;
			}

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

			SendResponse(response, new VariablesResponseBody(variables));
		}

		public override void Threads(Response response, dynamic args)
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
			SendResponse(response, new ThreadsResponseBody(threads));
		}

		public override void Evaluate(Response response, dynamic args)
		{
			string error = null;

			var expression = getString(args, "expression");
			if (expression == null) {
				error = "expression missing";
			} else {
				int frameId = getInt(args, "frameId", -1);
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
							SendResponse(response, new EvaluateResponseBody(val.DisplayValue, handle));
							return;
						}
					}
					else {
						error = "invalid expression";
					}
				}
				else {
					error = "no active stackframe";
				}
			}
			SendErrorResponse(response, 3014, "Evaluate request failed ({_reason}).", new { _reason = error } );
		}

		//---- private ------------------------------------------

		private void SendOutput(string category, string data) {
			if (!String.IsNullOrEmpty(data)) {
				if (data[data.Length-1] != '\n') {
					data += '\n';
				}
				SendEvent(new OutputEvent(category, data));
			}
		}

		private void Terminate(string reason) {
			if (!_terminated) {

				// wait until we've seen the end of stdout and stderr
				for (int i = 0; i < 100 && (_stdoutEOF == false || _stderrEOF == false); i++) {
					System.Threading.Thread.Sleep(100);
				}

				SendEvent(new TerminatedEvent());

				_terminated = true;
				_process = null;
			}
		}

		private StoppedEvent CreateStoppedEvent(string reason, ThreadInfo ti, string text = null)
		{
			return new StoppedEvent((int)ti.Id, reason, text);
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

		private static bool getBool(dynamic container, string propertyName, bool dflt = false)
		{
			try {
				return (bool)container[propertyName];
			}
			catch (Exception) {
				// ignore and return default value
			}
			return dflt;
		}

		private static int getInt(dynamic container, string propertyName, int dflt = 0)
		{
			try {
				return (int)container[propertyName];
			}
			catch (Exception) {
				// ignore and return default value
			}
			return dflt;
		}

		private static string getString(dynamic args, string property, string dflt = null)
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
