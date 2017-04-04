/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Mono.Debugging.Client;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Utilities;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace VSCodeDebug
{
	public class MonoDebugSession : DebugAdapterBase
	{
		private const string MONO = "mono";
		private readonly string[] MONO_EXTENSIONS = {
			".cs", ".csx",
			".fs", ".fsi", ".ml", ".mli", ".fsx", ".fsscript"
		};
		private const int MAX_CHILDREN = 100;
		private const int MAX_CONNECTION_ATTEMPTS = 10;
		private const int CONNECTION_ATTEMPT_INTERVAL = 500;

		private System.Threading.AutoResetEvent _resumeEvent = new System.Threading.AutoResetEvent(false);
		private bool _debuggeeExecuting;
		private readonly object _lock = new object();
		private Mono.Debugging.Soft.SoftDebuggerSession _session;
		private volatile bool _debuggeeKilled = true;
		private ProcessInfo _activeProcess;
		private Mono.Debugging.Client.StackFrame _activeFrame;
		private long _nextBreakpointId;
		private SortedDictionary<long, BreakEvent> _breakpoints;
		private List<Catchpoint> _catchpoints;
		private DebuggerSessionOptions _debuggerSessionOptions;

		private System.Diagnostics.Process _process;
		private Handles<ObjectValue[]> _variableHandles;
		private Handles<Mono.Debugging.Client.StackFrame> _frameHandles;
		private ObjectValue _exception;
		private Dictionary<int, Thread> _seenThreads = new Dictionary<int, Thread>();
		private bool _attachMode;
		private bool _terminated;
		private bool _stderrEOF = true;
		private bool _stdoutEOF = true;

		private bool _clientLinesStartAt1 = true;
		private bool _clientPathsAreURI = true;


		public MonoDebugSession(Stream inputStream, Stream outputStream)
		{
			_variableHandles = new Handles<ObjectValue[]>();
			_frameHandles = new Handles<Mono.Debugging.Client.StackFrame>();
			_seenThreads = new Dictionary<int, Thread>();

			_debuggerSessionOptions = new DebuggerSessionOptions {
				EvaluationOptions = EvaluationOptions.DefaultOptions
			};

			_session = new Mono.Debugging.Soft.SoftDebuggerSession();
			_session.Breakpoints = new BreakpointStore();

			_breakpoints = new SortedDictionary<long, BreakEvent>();
			_catchpoints = new List<Catchpoint>();

			DebuggerLoggingService.CustomLogger = new CustomLogger();

			_session.ExceptionHandler = ex => {
				return true;
			};

			_session.LogWriter = (isStdErr, text) => {
			};

			_session.TargetStopped += (sender, e) => {
				Stopped();
				Protocol.SendEvent(CreateStoppedEvent(StoppedEvent.ReasonValue.Step, e.Thread));
				_resumeEvent.Set();
			};

			_session.TargetHitBreakpoint += (sender, e) => {
				Stopped();
				Protocol.SendEvent(CreateStoppedEvent(StoppedEvent.ReasonValue.Breakpoint, e.Thread));
				_resumeEvent.Set();
			};

			_session.TargetExceptionThrown += (sender, e) => {
				Stopped();
				var ex = DebuggerActiveException();
				if (ex != null) {
					_exception = ex.Instance;
					Protocol.SendEvent(CreateStoppedEvent(StoppedEvent.ReasonValue.Exception, e.Thread, ex.Message));
				}
				_resumeEvent.Set();
			};

			_session.TargetUnhandledException += (sender, e) => {
				Stopped ();
				var ex = DebuggerActiveException();
				if (ex != null) {
					_exception = ex.Instance;
					Protocol.SendEvent(CreateStoppedEvent(StoppedEvent.ReasonValue.Exception, e.Thread, ex.Message));
				}
				_resumeEvent.Set();
			};

			_session.TargetStarted += (sender, e) => {
				_activeFrame = null;
			};

			_session.TargetReady += (sender, e) => {
				_activeProcess = _session.GetProcesses().SingleOrDefault();
			};

			_session.TargetExited += (sender, e) => {

				DebuggerKill();

				_debuggeeKilled = true;

				Terminate("target exited");

				_resumeEvent.Set();
			};

			_session.TargetInterrupted += (sender, e) => {
				_resumeEvent.Set();
			};

			_session.TargetEvent += (sender, e) => {
			};

			_session.TargetThreadStarted += (sender, e) => {
				int tid = (int)e.Thread.Id;
				lock (_seenThreads) {
					_seenThreads[tid] = new Thread(tid, e.Thread.Name);
				}
				Protocol.SendEvent(new ThreadEvent(ThreadEvent.ReasonValue.Started, tid));
			};

			_session.TargetThreadStopped += (sender, e) => {
				int tid = (int)e.Thread.Id;
				lock (_seenThreads) {
					_seenThreads.Remove(tid);
				}
				Protocol.SendEvent(new ThreadEvent(ThreadEvent.ReasonValue.Exited, tid));
			};

			_session.OutputWriter = (isStdErr, text) => {
				SendOutput(isStdErr ? OutputEvent.CategoryValue.Stderr : OutputEvent.CategoryValue.Stdout, text);
			};

			InitializeProtocolClient(inputStream, outputStream);
		}

		protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments)
		{
			if (arguments.LinesStartAt1 == true) {
				this._clientLinesStartAt1 = true;
			}

			switch (arguments.PathFormat) {
			case InitializeArguments.PathFormatValue.Uri:
				_clientPathsAreURI = true;
				break;
			case InitializeArguments.PathFormatValue.Path:
				_clientPathsAreURI = false;
				break;
			default:
				return (InitializeResponse) ErrorResponse(1015, "initialize: bad value '{_format}' for pathFormat", new { _format = arguments.PathFormat });
			}

			OperatingSystem os = Environment.OSVersion;
			if (os.Platform != PlatformID.MacOSX && os.Platform != PlatformID.Unix && os.Platform != PlatformID.Win32NT) {
				return (InitializeResponse) ErrorResponse(3000, "Mono Debug is not supported on this platform ({_platform}).", new { _platform = os.Platform.ToString() }, true, true);
			}

			// Mono Debug is ready to accept breakpoints immediately
			Protocol.SendEvent(new InitializedEvent());

			return new InitializeResponse(
				// This debug adapter does not need the configurationDoneRequest.
				supportsConfigurationDoneRequest: false,

				// This debug adapter does not support function breakpoints.
				supportsFunctionBreakpoints: false,

				// This debug adapter doesn't support conditional breakpoints.
				supportsConditionalBreakpoints: false,

				// This debug adapter does not support a side effect free evaluate request for data hovers.
				supportsEvaluateForHovers: false,

				// This debug adapter does not support exception breakpoint filters
				exceptionBreakpointFilters: new List<ExceptionBreakpointsFilter>()
			);
		}

		protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments)
		{
			_attachMode = false;

			JToken eo = null;
			if (arguments.ConfigurationProperties.TryGetValue("__exceptionOptions", out eo) && eo != null) {
				SetExceptionBreakpoints(null);
			}

			// validate argument 'program'
			var programPath = arguments.ConfigurationProperties.GetValueAsString("program");
			if (string.IsNullOrEmpty(programPath)) {
				return (LaunchResponse) ErrorResponse(3001, "Property 'program' is missing or empty.", null);
			}
			programPath = ConvertClientPathToDebugger(programPath);
			if (!File.Exists(programPath) && !Directory.Exists(programPath)) {
				return (LaunchResponse) ErrorResponse(3002, "Program '{path}' does not exist.", new { path = programPath });
			}

			// validate argument 'cwd'
			var workingDirectory = arguments.ConfigurationProperties.GetValueAsString("cwd");
			if (workingDirectory != null) {
				workingDirectory = workingDirectory.Trim();
				if (workingDirectory.Length == 0) {
					return (LaunchResponse) ErrorResponse(3003, "Property 'cwd' is empty.");
				}
				workingDirectory = ConvertClientPathToDebugger(workingDirectory);
				if (!Directory.Exists(workingDirectory)) {
					return (LaunchResponse) ErrorResponse(3004, "Working directory '{path}' does not exist.", new { path = workingDirectory });
				}
			}

			// validate argument 'runtimeExecutable'
			var runtimeExecutable = arguments.ConfigurationProperties.GetValueAsString("runtimeExecutable");
			if (runtimeExecutable != null) {
				runtimeExecutable = runtimeExecutable.Trim();
				if (runtimeExecutable.Length == 0) {
					return (LaunchResponse) ErrorResponse(3005, "Property 'runtimeExecutable' is empty.");
				}
				runtimeExecutable = ConvertClientPathToDebugger(runtimeExecutable);
				if (!File.Exists(runtimeExecutable)) {
					return (LaunchResponse) ErrorResponse(3006, "Runtime executable '{path}' does not exist.", new { path = runtimeExecutable });
				}
			}

			// validate argument 'env'
			Dictionary<string, object> env = null;
			var environmentVariables = arguments.ConfigurationProperties.GetValueAsObject("env");
			if (environmentVariables != null) {
				env = new Dictionary<string, object>();
				foreach (var entry in environmentVariables) {
					env.Add(entry.Key, (string)entry.Value);
				}
				if (env.Count == 0) {
					env = null;
				}
			}

			const string host = "127.0.0.1";
			int port = Utilities.FindFreePort(55555);

			string mono_path = runtimeExecutable;
			if (mono_path == null) {
				if (!Utilities.IsOnPath(MONO)) {
					return (LaunchResponse) ErrorResponse(3011, "Can't find runtime '{_runtime}' on PATH.", new { _runtime = MONO });
				}
				mono_path = MONO;     // try to find mono through PATH
			}


			var cmdLine = new List<string>();

			bool debug = false;
			var noDebug = arguments.ConfigurationProperties.GetValueAsBool("noDebug");
			if (noDebug == null || (bool)noDebug) {
				debug = true;
				cmdLine.Add("--debug");
				cmdLine.Add(string.Format("--debugger-agent=transport=dt_socket,server=y,address={0}:{1}", host, port));
			}

			// add 'runtimeArgs'
			JToken rargs = null;
			if (arguments.ConfigurationProperties.TryGetValue("runtimeArgs", out rargs) && rargs != null) {
				string[] runtimeArguments = rargs.ToObject<string[]>();
				if (runtimeArguments != null && runtimeArguments.Length > 0) {
					cmdLine.AddRange(runtimeArguments);
				}
			}

			// add 'program'
			if (workingDirectory == null) {
				// if no working dir given, we use the direct folder of the executable
				workingDirectory = Path.GetDirectoryName(programPath);
				cmdLine.Add(Path.GetFileName(programPath));
			}
			else {
				// if working dir is given and if the executable is within that folder, we make the program path relative to the working dir
				cmdLine.Add(Utilities.MakeRelativePath(workingDirectory, programPath));
			}

			// add 'args'
			JToken args = null;
			if (arguments.ConfigurationProperties.TryGetValue("args", out args) && args != null) {
				string[] arguments0 = args.ToObject<string[]>();
				if (arguments0 != null && arguments0.Length > 0) {
					cmdLine.AddRange(arguments0);
				}
			}

			// what console?
			var console = arguments.ConfigurationProperties.GetValueAsString("console");
			if (console == null) {
				// continue to read the deprecated "externalConsole" attribute
				var externalConsole = arguments.ConfigurationProperties.GetValueAsBool("externalConsole");
				if (externalConsole != null && (bool)externalConsole) {
					console = "externalTerminal";
				}
			}

			if (console == "externalTerminal" || console == "integratedTerminal") {

				cmdLine.Insert(0, mono_path);

				var cmd = new RunInTerminalRequest(workingDirectory, cmdLine, console == "integratedTerminal" ? RunInTerminalArguments.KindValue.Integrated : RunInTerminalArguments.KindValue.External, "Node Debug Console", env);

				if (false) {
					// this blocks:
					var t = new TaskCompletionSource<RunInTerminalResponse>();
					Protocol.SendClientRequest(cmd,
						(RunInTerminalArguments a, RunInTerminalResponse response) => t.SetResult(response),
						(RunInTerminalArguments a, ProtocolException exception) => t.TrySetException(exception)
					);
					var resp = t.Task.Result;
				} else {
					// this works:
					Protocol.SendClientRequest(cmd,
						  (RunInTerminalArguments a, RunInTerminalResponse response) => { Console.WriteLine("OK"); },
						  (RunInTerminalArguments a, ProtocolException exception) => { Console.WriteLine("Error"); }
					);
				}

			} else { // internalConsole

				_process = new System.Diagnostics.Process();
				_process.StartInfo.CreateNoWindow = true;
				_process.StartInfo.UseShellExecute = false;
				_process.StartInfo.WorkingDirectory = workingDirectory;
				_process.StartInfo.FileName = mono_path;
				_process.StartInfo.Arguments = Utilities.ConcatArgs(cmdLine.ToArray());

				_stdoutEOF = false;
				_process.StartInfo.RedirectStandardOutput = true;
				_process.OutputDataReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) => {
					if (e.Data == null) {
						_stdoutEOF = true;
					}
					SendOutput(OutputEvent.CategoryValue.Stdout, e.Data);
				};

				_stderrEOF = false;
				_process.StartInfo.RedirectStandardError = true;
				_process.ErrorDataReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) => {
					if (e.Data == null) {
						_stderrEOF = true;
					}
					SendOutput(OutputEvent.CategoryValue.Stderr, e.Data);
				};

				_process.EnableRaisingEvents = true;
				_process.Exited += (object sender, EventArgs e) => {
					Terminate("runtime process exited");
				};

				if (env != null) {
					// we cannot set the env vars on the process StartInfo because we need to set StartInfo.UseShellExecute to true at the same time.
					// instead we set the env vars on MonoDebug itself because we know that MonoDebug lives as long as a debug session.
					foreach (var entry in env) {
						Environment.SetEnvironmentVariable(entry.Key, (string) entry.Value);
					}
				}

				var cmd = string.Format("{0} {1}", mono_path, _process.StartInfo.Arguments);
				SendOutput(OutputEvent.CategoryValue.Console, cmd);

				try {
					_process.Start();
					_process.BeginOutputReadLine();
					_process.BeginErrorReadLine();
				}
				catch (Exception e) {
					return (LaunchResponse) ErrorResponse(3012, "Can't launch terminal ({reason}).", new { reason = e.Message });
				}
			}

			if (debug) {
				Connect(IPAddress.Parse(host), port);
			}

			// SendResponse(response);

			if (_process == null && !debug) {
				// we cannot track mono runtime process so terminate this session
				Terminate("cannot track mono runtime");
			}

			return new LaunchResponse();
		}

		protected override AttachResponse HandleAttachRequest(AttachArguments arguments)
		{
			_attachMode = true;

			//SetExceptionBreakpoints(arguments.__exceptionOptions);

			// validate argument 'address'
			var host = arguments.ConfigurationProperties.GetValueAsString("address");
			if (string.IsNullOrEmpty(host)) {
				return (AttachResponse) ErrorResponse(3007, "Property 'address' is missing or empty.");
			}

			// validate argument 'port'
			var port = arguments.ConfigurationProperties.GetValueAsInt("port");
			if (port != null) {
				return (AttachResponse) ErrorResponse(3008, "Property 'port' is missing.");
			}

			IPAddress address = Utilities.ResolveIPAddress(host);
			if (address == null) {
				return (AttachResponse) ErrorResponse(3013, "Invalid address '{address}'.", new { address = address });
			}

			Connect(address, (int) port);

			//SendResponse(response);
			return new AttachResponse();
		}

		protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments)
		{
			if (_attachMode) {

				lock (_lock) {
					if (_session != null) {
						_debuggeeExecuting = true;
						_breakpoints.Clear();
						_session.Breakpoints.Clear();
						_session.Continue();
						_session = null;
					}
				}

			} else {
				// Let's not leave dead Mono processes behind...
				if (_process != null) {
					_process.Kill();
					_process = null;
				} else {
					PauseDebugger();
					DebuggerKill();

					while (!_debuggeeKilled) {
						System.Threading.Thread.Sleep(10);
					}
				}
			}

			//SendResponse(response);
			return new DisconnectResponse();
		}

		protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments)
		{
			WaitForSuspend();
			//SendResponse(response);
			lock (_lock) {
				if (_session != null && !_session.IsRunning && !_session.HasExited) {
					_session.Continue();
					_debuggeeExecuting = true;
				}
			}
			return new ContinueResponse();
		}

		protected override NextResponse HandleNextRequest(NextArguments arguments)
		{
			WaitForSuspend();
			//SendResponse(response);
			lock (_lock) {
				if (_session != null && !_session.IsRunning && !_session.HasExited) {
					_session.NextLine();
					_debuggeeExecuting = true;
				}
			}
			return new NextResponse();
		}

		protected override StepInResponse HandleStepInRequest(StepInArguments arguments)
		{
			WaitForSuspend();
			//SendResponse(response);
			lock (_lock) {
				if (_session != null && !_session.IsRunning && !_session.HasExited) {
					_session.StepLine();
					_debuggeeExecuting = true;
				}
			}
			return new StepInResponse();
		}

		protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments)
		{
			WaitForSuspend();
			//SendResponse(response);
			lock (_lock) {
				if (_session != null && !_session.IsRunning && !_session.HasExited) {
					_session.Finish();
					_debuggeeExecuting = true;
				}
			}
			return new StepOutResponse();
		}

		protected override PauseResponse HandlePauseRequest(PauseArguments arguments)
		{
			//SendResponse(response);
			PauseDebugger();
			return new PauseResponse();
		}

		protected override SetExceptionBreakpointsResponse HandleSetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments arguments)
		{
			SetExceptionBreakpoints(arguments.ExceptionOptions);
			//SendResponse(response);
			return new SetExceptionBreakpointsResponse();
		}

		protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
		{
			string path = null;
			if (arguments.Source != null) {
				string p = arguments.Source.Path;
				if (p != null && p.Trim().Length > 0) {
					path = p;
				}
			}
			if (path == null) {
				return (SetBreakpointsResponse) ErrorResponse(3010, "setBreakpoints: property 'source' is empty or misformed", null, false, true);
			}
			path = ConvertClientPathToDebugger(path);

			if (!HasMonoExtension(path)) {
				// we only support breakpoints in files mono can handle
				//SendResponse(response, new SetBreakpointsResponseBody());
				//return;
				return new SetBreakpointsResponse();
			}

			var clientLines = arguments.Lines;
			HashSet<int> lin = new HashSet<int>();
			for (int i = 0; i < clientLines.Count; i++) {
				lin.Add(ConvertClientLineToDebugger(clientLines[i]));
			}

			// find all breakpoints for the given path and remember their id and line number
			var bpts = new List<Tuple<int, int>>();
			foreach (var be in _breakpoints) {
				var bp = be.Value as Mono.Debugging.Client.Breakpoint;
				if (bp != null && bp.FileName == path) {
					bpts.Add(new Tuple<int,int>((int)be.Key, bp.Line));
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
					if (_breakpoints.TryGetValue(bpt.Item1, out b)) {
						_breakpoints.Remove(bpt.Item1);
						_session.Breakpoints.Remove(b);
					}
				}
			}

			for (int i = 0; i < clientLines.Count; i++) {
				var l = ConvertClientLineToDebugger(clientLines[i]);
				if (!lin2.Contains(l)) {
					var id = _nextBreakpointId++;
					_breakpoints.Add(id, _session.Breakpoints.Add(path, l));
					// Console.WriteLine("added bpt #{0} for line {1}", id, l);
				}
			}

			var breakpoints = new List<Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Breakpoint>();
			foreach (var l in clientLines) {
				breakpoints.Add(new Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Breakpoint(true, l));
			}

			//SendResponse(response, new SetBreakpointsResponseBody(breakpoints));
			return new SetBreakpointsResponse(breakpoints: breakpoints);
		}

		protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments)
		{
			int maxLevels = arguments.Levels != null ? (int) arguments.Levels : 10;
			int threadReference = arguments.ThreadId;

			WaitForSuspend();
			var stackFrames = new List<Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.StackFrame>();

			ThreadInfo thread = DebuggerActiveThread();
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
					var path = frame.SourceLocation.FileName;

					stackFrames.Add(new Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.StackFrame(
						id: _frameHandles.Create(frame),
						name: frame.SourceLocation.MethodName,
						source: new Source(Path.GetFileName(path), ConvertDebuggerPathToClient(path)),
						line: ConvertDebuggerLineToClient(frame.SourceLocation.Line),
						column: 0)
					);
				}
			}

			//SendResponse(response, new StackTraceResponseBody(stackFrames));
			return new StackTraceResponse(stackFrames: stackFrames);
		}

		protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments)
		{
			int frameId = arguments.FrameId;
			var frame = _frameHandles.Get(frameId, null);

			var scopes = new List<Scope>();

			if (frame.Index == 0 && _exception != null) {
				scopes.Add(new Scope("Exception", _variableHandles.Create(new ObjectValue[] { _exception }), false));
			}

			var locals = new[] { frame.GetThisReference() }.Concat(frame.GetParameters()).Concat(frame.GetLocalVariables()).Where(x => x != null).ToArray();
			if (locals.Length > 0) {
				scopes.Add(new Scope("Local", _variableHandles.Create(locals), false));
			}

			//SendResponse(response, new ScopesResponseBody(scopes));
			return new ScopesResponse(scopes: scopes);
		}

		protected override VariablesResponse HandleVariablesRequest(VariablesArguments arguments)
		{
			int reference = arguments.VariablesReference;
			if (reference == -1) {
				return (VariablesResponse) ErrorResponse(3009, "variables: property 'variablesReference' is missing", null, false, true);
			}

			WaitForSuspend();
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
						System.Threading.WaitHandle.WaitAll(children.Select(x => x.WaitHandle).ToArray());
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
						variables.Add(new Variable(name: "...", value: null, variablesReference: 0));
					}
				}
			}

			//SendResponse(response, new VariablesResponseBody(variables));
			return new VariablesResponse(variables: variables);
		}

		protected override ThreadsResponse HandleThreadsRequest(ThreadsArguments arguments)
		{
			var threads = new List<Thread>();
			var process = _activeProcess;
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
			//SendResponse(response, new ThreadsResponseBody(threads));
			return new ThreadsResponse(threads: threads);
		}

		protected override EvaluateResponse HandleEvaluateRequest(EvaluateArguments arguments)
		{
			string error = null;

			var expression = arguments.Expression;
			if (expression == null) {
				error = "expression missing";
			} else {
				var frameId = arguments.FrameId;
				if (frameId != null) {
					var frame = _frameHandles.Get((int)frameId, null);
					if (frame != null) {
						if (frame.ValidateExpression(expression)) {
							var val = frame.GetExpressionValue(expression, _debuggerSessionOptions.EvaluationOptions);
							val.WaitHandle.WaitOne();

							var flags = val.Flags;
							if (flags.HasFlag(ObjectValueFlags.Error) || flags.HasFlag(ObjectValueFlags.NotSupported)) {
								error = val.DisplayValue;
								if (error.IndexOf("reference not available in the current evaluation context", StringComparison.Ordinal) > 0) {
									error = "not available";
								}
							} else if (flags.HasFlag(ObjectValueFlags.Unknown)) {
								error = "invalid expression";
							} else if (flags.HasFlag(ObjectValueFlags.Object) && flags.HasFlag(ObjectValueFlags.Namespace)) {
								error = "not available";
							} else {
								int handle = 0;
								if (val.HasChildren) {
									handle = _variableHandles.Create(val.GetAllChildren());
								}
								//SendResponse(response, new EvaluateResponseBody(val.DisplayValue, handle));
								//return;
								return new EvaluateResponse(val.DisplayValue, handle);
							}
						} else {
							error = "invalid expression";
						}
					} else {
						error = "no active stackframe";
					}
				} else {
					error = "missing frameId";
				}
			}
			return (EvaluateResponse) ErrorResponse(3014, "Evaluate request failed ({_reason}).", new { _reason = error } );
		}

		//---- private ------------------------------------------

		public ResponseBody ErrorResponse(int id, string format, dynamic arguments = null, bool user = true, bool telemetry = false)
		{
			var msg = new Message(id, format, arguments, user, telemetry);

			//var message = Utilities.ExpandVariables(msg.Format, msg.Variables);

			return new ErrorResponse(msg);
		}

		private void SetExceptionBreakpoints(List<ExceptionOptions> exceptionOptions)
		{
			if (exceptionOptions != null) {

				// clear all existig catchpoints
				foreach (var cp in _catchpoints) {
					_session.Breakpoints.Remove(cp);
				}
				_catchpoints.Clear();

				foreach (var exception in exceptionOptions) {

					string exName = null;
					var exBreakMode = exception.BreakMode;

					if (exception.Path != null) {
						var paths = exception.Path;
						var path = paths[0];
						if (path.Names != null) {
							var names = path.Names;
							if (names.Count > 0) {
								exName = names[0];
							}
						}
					}

					if (exName != null && exBreakMode == ExceptionBreakMode.Always) {
						_catchpoints.Add(_session.Breakpoints.AddCatchpoint(exName));
					}
				}
			}
		}

		private void SendOutput(OutputEvent.CategoryValue category, string data) {
			if (!string.IsNullOrEmpty(data)) {
				if (data[data.Length-1] != '\n') {
					data += '\n';
				}
				Protocol.SendEvent(new OutputEvent(category: category, output: data));
			}
		}

		private void Terminate(string reason) {
			if (!_terminated) {

				// wait until we've seen the end of stdout and stderr
				for (int i = 0; i < 100 && (_stdoutEOF == false || _stderrEOF == false); i++) {
					System.Threading.Thread.Sleep(100);
				}

				Protocol.SendEvent(new TerminatedEvent());

				_terminated = true;
				_process = null;
			}
		}

		private StoppedEvent CreateStoppedEvent(StoppedEvent.ReasonValue reason, ThreadInfo ti, string text = null)
		{
			return new StoppedEvent(reason: reason, threadId: (int)ti.Id, text: text);
		}

		private ThreadInfo FindThread(int threadReference)
		{
			if (_activeProcess != null) {
				foreach (var t in _activeProcess.GetThreads()) {
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
			var dv = v.DisplayValue;
			if (dv.Length > 1 && dv [0] == '{' && dv [dv.Length - 1] == '}') {
				dv = dv.Substring (1, dv.Length - 2);
			}
			return new Variable(name: v.Name, value: dv, type: v.TypeName, variablesReference: v.HasChildren ? _variableHandles.Create(v.GetAllChildren()) : 0);
		}

		private bool HasMonoExtension(string path)
		{
			foreach (var e in MONO_EXTENSIONS) {
				if (path.EndsWith(e, StringComparison.Ordinal)) {
					return true;
				}
			}
			return false;
		}

		//-----------------------

		private void WaitForSuspend()
		{
			if (_debuggeeExecuting) {
				_resumeEvent.WaitOne();
				_debuggeeExecuting = false;
			}
		}

		private ThreadInfo DebuggerActiveThread()
		{
			lock (_lock) {
				return _session == null ? null : _session.ActiveThread;
			}
		}

		private Backtrace DebuggerActiveBacktrace() {
			var thr = DebuggerActiveThread();
			return thr == null ? null : thr.Backtrace;
		}

		private Mono.Debugging.Client.StackFrame DebuggerActiveFrame() {
			var f = _activeFrame;
			if (f != null)
				return f;

			var bt = DebuggerActiveBacktrace();
			if (bt != null)
				return _activeFrame = bt.GetFrame(0);

			return null;
		}

		private ExceptionInfo DebuggerActiveException() {
			var bt = DebuggerActiveBacktrace();
			return bt == null ? null : bt.GetFrame(0).GetException();
		}

		private void Connect(IPAddress address, int port)
		{
			lock (_lock) {

				_debuggeeKilled = false;

				var args0 = new Mono.Debugging.Soft.SoftDebuggerConnectArgs(string.Empty, address, port) {
					MaxConnectionAttempts = MAX_CONNECTION_ATTEMPTS,
					TimeBetweenConnectionAttempts = CONNECTION_ATTEMPT_INTERVAL
				};

				_session.Run(new Mono.Debugging.Soft.SoftDebuggerStartInfo(args0), _debuggerSessionOptions);

				_debuggeeExecuting = true;
			}
		}

		private void PauseDebugger()
		{
			lock (_lock) {
				if (_session != null && _session.IsRunning)
					_session.Stop();
			}
		}

		private void DebuggerKill()
		{
			lock (_lock) {
				if (_session != null) {

					_debuggeeExecuting = true;

					if (!_session.HasExited)
						_session.Exit();

					_session.Dispose();
					_session = null;
				}
			}
		}

		private int ConvertDebuggerLineToClient(int line)
		{
			return _clientLinesStartAt1 ? line : line - 1;
		}

		private int ConvertClientLineToDebugger(int line)
		{
			return _clientLinesStartAt1 ? line : line + 1;
		}

		private string ConvertDebuggerPathToClient(string path)
		{
			if (_clientPathsAreURI) {
				try {
					var uri = new Uri(path);
					return uri.AbsoluteUri;
				} catch {
					return null;
				}
			} else {
				return path;
			}
		}

		private string ConvertClientPathToDebugger(string clientPath)
		{
			if (clientPath == null) {
				return null;
			}

			if (_clientPathsAreURI) {
				if (Uri.IsWellFormedUriString(clientPath, UriKind.Absolute)) {
					Uri uri = new Uri(clientPath);
					return uri.LocalPath;
				}
				Console.Error.WriteLine("path not well formed: '{0}'", clientPath);
				return null;
			} else {
				return clientPath;
			}
		}
	}
}
