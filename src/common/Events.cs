/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/

namespace OpenDebug
{
	public class DebugEvent
	{
		public string type { get; set; }

		public DebugEvent(string typ) {
			type = typ;
		}
	}

	public class InitializedEvent : DebugEvent
	{
		public InitializedEvent() : base("initialized") {
		}
	}

	public class StoppedEvent : DebugEvent
	{
		public int threadId { get; set; }
		public string reason { get; set; }
		public Source source { get; set; }
		public int line { get; set; }
		public int column { get; set; }
		public string text { get; set; }

		public StoppedEvent(string reasn, Source src, int ln, int col = 0, string txt = null, int tid = 0) : base("stopped") {
			reason = reasn;
			source = src;
			line = ln;
			column = col;
			text = txt;
			threadId = tid;
		}
	}

	public class ExitedEvent : DebugEvent
	{
		public int exitCode { get; set; }

		public ExitedEvent(int exCode) : base("exited") {
			exitCode = exCode;
		}
	}

	public class TerminatedEvent : DebugEvent
	{
		public TerminatedEvent() : base("terminated") {
		}
	}

	public class ThreadEvent : DebugEvent
	{
		public string reason { get; set; }
		public int threadId { get; set; }

		public ThreadEvent(string reasn, int tid) : base("thread") {
			reason = reasn;
			threadId = tid;
		}
	}
}
