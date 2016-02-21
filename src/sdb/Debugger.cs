//
// The MIT License (MIT)
//
// Copyright (c) 2014 Alex Rønne Petersen
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using Mono.Debugging.Client;
using Mono.Debugging.Soft;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;

namespace Mono.Debugger.Client
{
	public static class Debugger
	{
		static readonly object _lock = new object();

		static Debugger()
		{
			EnsureCreated();
			ResetOptions();
			ResetState();

			_debuggeeKilled = true;

			DebuggerLoggingService.CustomLogger = new CustomLogger();
		}

		static SoftDebuggerSession _session;

		public static FileInfo CurrentExecutable { get; private set; }

		public static IPAddress CurrentAddress { get; private set; }

		public static int CurrentPort { get; private set; }

		public static DebuggerSessionOptions Options { get; private set; }

		public static string WorkingDirectory { get; set; }

		public static string Arguments { get; set; }

		public static SortedDictionary<string, string> EnvironmentVariables { get; private set; }

		public static SortedDictionary<long, string> Watches { get; private set; }

		public static SortedDictionary<long, BreakEvent> Breakpoints { get; private set; }

		public static SortedDictionary<string, string> Aliases { get; private set; }

		public static BreakpointStore BreakEvents { get; private set; }

		public static bool DebuggeeKilled
		{
			get { return _debuggeeKilled; }
			set { _debuggeeKilled = value; }
		}

		static volatile bool _debuggeeKilled;

		static long _nextWatchId;

		static long _nextBreakpointId;


		public static State State
		{
			get
			{
				lock (_lock)
				{
					if (_session == null || _session.HasExited || !_session.IsConnected)
						return State.Exited;

					return _session.IsRunning ? State.Running : State.Suspended;
				}
			}
		}

		static volatile SessionKind _kind;

		public static SessionKind Kind
		{
			get { return _kind; }
			set { _kind = value; }
		}

		static ProcessInfo _activeProcess;

		public static ProcessInfo ActiveProcess
		{
			get { return _activeProcess; }
		}

		public static ThreadInfo ActiveThread
		{
			get
			{
				lock (_lock)
					return _session == null ? null : _session.ActiveThread;
			}
		}

		public static Backtrace ActiveBacktrace
		{
			get
			{
				var thr = ActiveThread;

				return thr == null ? null : thr.Backtrace;
			}
		}

		static StackFrame _activeFrame;

		public static StackFrame ActiveFrame
		{
			get
			{
				var f = _activeFrame;

				if (f != null)
					return f;

				var bt = ActiveBacktrace;

				if (bt != null)
					return _activeFrame = bt.GetFrame(0);

				return null;
			}
			set { _activeFrame = value; }
		}

		public static ExceptionInfo ActiveException
		{
			get
			{
				var bt = ActiveBacktrace;

				return bt == null ? null : bt.GetFrame(0).GetException();
			}
		}

		static void PrintException(string prefix, ExceptionInfo ex)
		{
			// HACK: Until we get a `WaitHandle` property on the
			// `Mono.Debugging.Client.ExceptionInfo` type...
			ex.Message.Discard();

			while (ex.Message == "Loading...")
				Thread.Sleep(10);

			Log.Error("{0}{1}: {2}", prefix, ex.Type, ex.Message);
		}

		static void PrintException(ExceptionInfo ex)
		{
			PrintException(string.Empty, ex);

			var prefix = "> ";
			var inner = ex;

			while ((inner = inner.InnerException) != null)
			{
				PrintException(prefix, inner);

				prefix = "--" + prefix;
			}
		}

		static string StringizeTarget()
		{
			if (CurrentExecutable != null)
				return CurrentExecutable.Name;

			if (CurrentAddress != null)
				return string.Format("{0}:{1}", CurrentAddress, CurrentPort);

			return "<none>";
		}

		public static Action<string, ThreadInfo, string> Callback { get; set; }

		static void EnsureCreated()
		{
			lock (_lock)
			{
				if (_session != null)
					return;

				_session = new SoftDebuggerSession();
				_session.Breakpoints = BreakEvents;

				_session.ExceptionHandler = ex =>
				{
					if (Configuration.Current.LogInternalErrors)
					{
						Log.Error("Internal debugger error:", ex.GetType());
						Log.Error(ex.ToString());
					}

					return true;
				};

				_session.LogWriter = (isStdErr, text) =>
				{
					if (Configuration.Current.LogRuntimeSpew)
						Log.NoticeSameLine("[Mono] {0}", text); // The string already has a line feed.
				};

				_session.OutputWriter = (isStdErr, text) =>
				{
					//lock (Log.Lock)
					//{
						if (Callback != null)
						{
							Callback.Invoke(isStdErr ? "ErrorOutput" : "Output", null, text);
						}
						else
						{
							if (isStdErr)
								Console.Error.Write(text);
							else
								Console.Write(text);
						}
					//}
				};

				_session.TypeResolverHandler += (identifier, location) =>
				{
					// I honestly have no idea how correct this is. I suspect you
					// could probably break it in some corner cases. It does make
					// something like `p Android.Runtime.JNIEnv.Handle` work,
					// though, which would otherwise have required `global::` to
					// be explicitly prepended.

					if (identifier == "__EXCEPTION_OBJECT__")
						return null;

					foreach (var loc in ActiveFrame.GetAllLocals())
						if (loc.Name == identifier)
							return null;

					return identifier;
				};

				_session.TargetEvent += (sender, e) =>
				{
					Log.Debug("Event: '{0}'", e.Type);
				};

				_session.TargetStarted += (sender, e) =>
				{
					_activeFrame = null;

					/*
					if (_showResumeMessage)
						Log.Notice("Inferior process '{0}' ('{1}') resumed",
							ActiveProcess.Id, StringizeTarget());
					*/
				};

				_session.TargetReady += (sender, e) =>
				{
					_activeProcess = _session.GetProcesses().SingleOrDefault();

					// The inferior process has launched, so we can safely
					// set our `SIGINT` handler without it interfering with
					// the inferior.
					CommandLine.SetControlCHandler();

					/*
					Log.Notice("Inferior process '{0}' ('{1}') started",
						ActiveProcess.Id, StringizeTarget());
					*/
				};

				_session.TargetStopped += (sender, e) =>
				{
					//Log.Notice("Inferior process '{0}' ('{1}') suspended",
					//	ActiveProcess.Id, StringizeTarget());
					//Log.Emphasis(Utilities.StringizeFrame(ActiveFrame, true));

					if (Callback != null)
					{
						Callback.Invoke("TargetStopped", e.Thread, null);
					}

					CommandLine.ResumeEvent.Set();
				};

				_session.TargetInterrupted += (sender, e) =>
				{
					Log.Notice("Inferior process '{0}' ('{1}') interrupted",
						ActiveProcess.Id, StringizeTarget());
					Log.Emphasis(Utilities.StringizeFrame(ActiveFrame, true));

					CommandLine.ResumeEvent.Set();
				};

				_session.TargetHitBreakpoint += (sender, e) =>
				{
					// var bp = e.BreakEvent as Breakpoint;
					// var fbp = e.BreakEvent as FunctionBreakpoint;

					/*
					if (fbp != null)
						Log.Notice("Hit method breakpoint on '{0}'", fbp.FunctionName);
					else
					{
						var cond = bp.ConditionExpression != null ?
							string.Format(" (condition '{0}' met)", bp.ConditionExpression) :
							string.Empty;

						Log.Notice("Hit breakpoint at '{0}:{1}'{2}", bp.FileName, bp.Line, cond);
					}

					Log.Emphasis(Utilities.StringizeFrame(ActiveFrame, true));
					*/

					if (Callback != null)
					{
						Callback.Invoke("TargetHitBreakpoint", e.Thread, null);
					}

					CommandLine.ResumeEvent.Set();
				};

				_session.TargetExited += (sender, e) =>
				{
					var p = ActiveProcess;

					/*
					// Can happen when a remote connection attempt fails.
					if (p == null)
					{
						if (_kind == SessionKind.Listening)
							Log.Notice("Listening socket closed");
						else if (_kind == SessionKind.Connected)
							Log.Notice("Connection attempt terminated");
						else
							Log.Notice("Failed to connect to '{0}'", StringizeTarget());
					}
					else
						Log.Notice("Inferior process '{0}' ('{1}') exited", ActiveProcess.Id, StringizeTarget());
					*/

					// Make sure we clean everything up on a normal exit.
					Kill();

					_debuggeeKilled = true;
					_kind = SessionKind.Disconnected;

					if (Callback != null)
					{
						Callback.Invoke("TargetExited", null, null);
					}

					CommandLine.ResumeEvent.Set();
				};

				_session.TargetExceptionThrown += (sender, e) =>
				{
					var ex = ActiveException;

					//Log.Notice("Trapped first-chance exception of type '{0}'", ex.Type);
					//Log.Emphasis(Utilities.StringizeFrame(ActiveFrame, true));

					PrintException(ex);

					if (Callback != null)
					{
						Callback.Invoke("TargetExceptionThrown", e.Thread, null);
					}

					CommandLine.ResumeEvent.Set();
				};

				_session.TargetUnhandledException += (sender, e) =>
				{
					var ex = ActiveException;

					//Log.Notice("Trapped unhandled exception of type '{0}'", ex.Type);
					//Log.Emphasis(Utilities.StringizeFrame(ActiveFrame, true));

					PrintException(ex);

					if (Callback != null)
					{
						Callback.Invoke("TargetUnhandledException", e.Thread, null);
					}

					CommandLine.ResumeEvent.Set();
				};

				_session.TargetThreadStarted += (sender, e) =>
				{
					//Log.Notice("Inferior thread '{0}' ('{1}') started",
					//	e.Thread.Id, e.Thread.Name);

					if (Callback != null)
					{
						Callback.Invoke("TargetThreadStarted", e.Thread, null);
					}
				};

				_session.TargetThreadStopped += (sender, e) =>
				{
					//Log.Notice("Inferior thread '{0}' ('{1}') exited",
					//	e.Thread.Id, e.Thread.Name);

					if (Callback != null)
					{
						Callback.Invoke("TargetThreadStopped", e.Thread, null);
					}
				};
			}
		}

		public static void Run(FileInfo file)
		{
			lock (_lock)
			{
				EnsureCreated();

				CurrentExecutable = file;
				CurrentAddress = null;
				CurrentPort = -1;

				_debuggeeKilled = false;
				_kind = SessionKind.Launched;

				var info = new SoftDebuggerStartInfo(Configuration.Current.RuntimePrefix,
					new Dictionary<string, string>(EnvironmentVariables))
				{
					Command = file.FullName,
					Arguments = Arguments,
					WorkingDirectory = WorkingDirectory,
					StartArgs =
					{
						MaxConnectionAttempts = Configuration.Current.MaxConnectionAttempts,
						TimeBetweenConnectionAttempts = Configuration.Current.ConnectionAttemptInterval
					}
				};

				// We need to ignore `SIGINT` while we start the inferior
				// process so that it inherits that signal disposition.
				CommandLine.UnsetControlCHandler();

				_session.Run(info, Options);

				CommandLine.InferiorExecuting = true;
			}
		}

		public static void Connect(IPAddress address, int port)
		{
			lock (_lock)
			{
				EnsureCreated();

				CurrentExecutable = null;
				CurrentAddress = address;
				CurrentPort = port;

				_debuggeeKilled = false;
				_kind = SessionKind.Connected;

				var args = new SoftDebuggerConnectArgs(string.Empty, address, port)
				{
					MaxConnectionAttempts = Configuration.Current.MaxConnectionAttempts,
					TimeBetweenConnectionAttempts = Configuration.Current.ConnectionAttemptInterval
				};

				_session.Run(new SoftDebuggerStartInfo(args), Options);

				CommandLine.InferiorExecuting = true;
			}
		}

		public static void Listen(IPAddress address, int port)
		{
			lock (_lock)
			{
				EnsureCreated();

				CurrentExecutable = null;
				CurrentAddress = address;
				CurrentPort = port;

				_debuggeeKilled = false;
				_kind = SessionKind.Listening;

				var args = new SoftDebuggerListenArgs(string.Empty, address, port);

				_session.Run(new SoftDebuggerStartInfo(args), Options);

				CommandLine.InferiorExecuting = true;
			}
		}

		public static void Pause()
		{
			lock (_lock)
				if (_session != null && _session.IsRunning)
					_session.Stop();
		}

		public static void Continue()
		{
			lock (_lock)
			{
				if (_session != null && !_session.IsRunning && !_session.HasExited)
				{
					_session.Continue();

					CommandLine.InferiorExecuting = true;
				}
			}
		}

		public static void Disconnect()
		{
			lock (_lock)
			{
				if (_session == null)
					return;

				CommandLine.InferiorExecuting = true;

				_kind = SessionKind.Disconnected;

				Breakpoints.Clear();
				BreakEvents.Clear();

				_session.Continue();

				_session = null;
			}
		}

		public static void Kill()
		{
			lock (_lock)
			{
				if (_session == null)
					return;

				CommandLine.InferiorExecuting = true;

				if (!_session.HasExited)
					_session.Exit();

				_session.Dispose();
				_session = null;
			}
		}

		public static void StepOverLine()
		{
			lock (_lock)
			{
				if (_session != null && !_session.IsRunning && !_session.HasExited)
				{
					_session.NextLine();

					CommandLine.InferiorExecuting = true;
				}
			}
		}

		public static void StepOverInstruction()
		{
			lock (_lock)
			{
				if (_session != null && !_session.IsRunning && !_session.HasExited)
				{
					_session.NextInstruction();

					CommandLine.InferiorExecuting = true;
				}
			}
		}

		public static void StepIntoLine()
		{
			lock (_lock)
			{
				if (_session != null && !_session.IsRunning && !_session.HasExited)
				{
					_session.StepLine();

					CommandLine.InferiorExecuting = true;
				}
			}
		}

		public static void StepIntoInstruction()
		{
			lock (_lock)
			{
				if (_session != null && !_session.IsRunning && !_session.HasExited)
				{
					_session.StepInstruction();

					CommandLine.InferiorExecuting = true;
				}
			}
		}

		public static void StepOutOfMethod()
		{
			lock (_lock)
			{
				if (_session != null && !_session.IsRunning && !_session.HasExited)
				{
					_session.Finish();

					CommandLine.InferiorExecuting = true;
				}
			}
		}

		public static void SetInstruction(int offset)
		{
			lock (_lock)
				if (_session != null && !_session.IsRunning && !_session.HasExited)
					_session.SetNextStatement(offset);
		}

		public static void SetLine(string fileName, int line)
		{
			lock (_lock)
				if (_session != null && !_session.IsRunning && !_session.HasExited)
					_session.SetNextStatement(fileName, line, 1);
		}

		public static long GetWatchId()
		{
			return _nextWatchId++;
		}

		public static long GetBreakpointId()
		{
			return _nextBreakpointId++;
		}

		public static void ResetState()
		{
			// No need to lock on this data.
			WorkingDirectory = Environment.CurrentDirectory;
			Arguments = string.Empty;
			EnvironmentVariables = new SortedDictionary<string, string>();
			Watches = new SortedDictionary<long, string>();
			_nextWatchId = 0;
			Breakpoints = new SortedDictionary<long, BreakEvent>();
			Aliases = new SortedDictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
			BreakEvents = new BreakpointStore();

			// Make sure breakpoints/catchpoints take effect.
			lock (_lock)
				if (_session != null)
					_session.Breakpoints = BreakEvents;
		}

		public static void ResetOptions()
		{
			// No need to lock on this data.

			Options = new DebuggerSessionOptions
			{
				EvaluationOptions = EvaluationOptions.DefaultOptions
			};

			Options.EvaluationOptions.UseExternalTypeResolver = true;
		}

		[Serializable]
		sealed class DebuggerState
		{
			public string WorkingDirectory { get; set; }

			public string Arguments { get; set; }

			public SortedDictionary<string, string> EnvironmentVariables { get; set; }

			public SortedDictionary<long, string> Watches { get; set; }

			public long NextWatchId { get; set; }

			public Dictionary<long, Tuple<BreakEvent, bool>> Breakpoints { get; set; }

			public long NextBreakpointId { get; set; }

			public ReadOnlyCollection<Catchpoint> Catchpoints { get; set; }

			public SortedDictionary<string, string> Aliases { get; set; }
		}

		public static void Write(FileInfo file)
		{
			var state = new DebuggerState
			{
				WorkingDirectory = WorkingDirectory,
				Arguments = Arguments,
				EnvironmentVariables = EnvironmentVariables,
				Watches = Watches,
				NextWatchId = _nextWatchId,
				Breakpoints = Breakpoints.ToDictionary(x => x.Key,
					x => Tuple.Create(x.Value, BreakEvents.Contains(x.Value))),
				NextBreakpointId = _nextBreakpointId,
				Catchpoints = BreakEvents.GetCatchpoints(),
				Aliases = Aliases,
			};

			try
			{
				using (var stream = file.Open(FileMode.Create, FileAccess.Write))
					new BinaryFormatter().Serialize(stream, state);
			}
			catch (Exception ex)
			{
				Log.Error("Could not write database file '{0}':", file);
				Log.Error(ex.ToString());
			}
		}

		public static void Read(FileInfo file)
		{
			DebuggerState state;

			try
			{
				using (var stream = file.Open(FileMode.Open, FileAccess.Read))
					state = (DebuggerState)new BinaryFormatter().Deserialize(stream);
			}
			catch (Exception ex)
			{
				Log.Error("Could not read database file '{0}':", file);
				Log.Error(ex.ToString());

				return;
			}

			ResetState();

			WorkingDirectory = state.WorkingDirectory;
			Arguments = state.Arguments;
			EnvironmentVariables = state.EnvironmentVariables;
			Watches = state.Watches;
			Aliases = state.Aliases;

			Breakpoints.Clear();
			BreakEvents.Clear();

			foreach (var kvp in state.Breakpoints)
			{
				Breakpoints.Add(kvp.Key, kvp.Value.Item1);

				if (kvp.Value.Item2)
					BreakEvents.Add(kvp.Value.Item1);
			}

			foreach (var cp in state.Catchpoints)
				BreakEvents.Add(cp);

			_nextWatchId = state.NextWatchId;
			_nextBreakpointId = state.NextBreakpointId;
		}
	}
}
