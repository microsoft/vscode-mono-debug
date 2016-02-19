/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using System.IO;

namespace OpenDebug
{
	public class Message {
		public int id { get; set; }
		public string format { get; set; }
		public dynamic variables { get; set; }

		public Message(int id, string format, dynamic variables = null) {
			this.id = id;
			this.format = format;
			this.variables = variables;
		}
	}

	public class StackFrame
	{
		public int id { get; set; }
		public Source source { get; set; }
		public int line { get; set; }
		public int column { get; set; }
		public string name { get; set; }

		public StackFrame(int i, string nm, Source src, int ln, int col) {
			id = i;
			source = src;
			line = ln;
			column = col;
			name = nm;
		}
	}

	public class Scope
	{
		public string name { get; set; }
		public int variablesReference { get; set; }
		public bool expensive { get; set; }

		public Scope(string nm, int rf, bool exp = false) {
			name = nm;
			variablesReference = rf;
			expensive = exp;
		}
	}

	public class Variable
	{
		public string name { get; set; }
		public string value { get; set; }
		public int variablesReference { get; set; }

		public Variable(string nm, string val, int rf = 0) {
			name = nm;
			value = val;
			variablesReference = rf;
		}
	}

	public class Thread
	{
		public int id { get; set; }
		public string name { get; set; }

		public Thread(int i, string nm) {
			id = i;
			if (nm == null || nm.Length == 0) {
				name = string.Format("Thread #{0}", id);
			}
			else {
				name = nm;
			}
		}
	}

	public class Source
	{
		public string name { get; set; }
		public string path { get; set; }
		public int sourceReference { get; set; }

		public Source(string nm, string pth, int rf = 0) {
			name = nm;
			path = pth;
			sourceReference = rf;
		}

		public Source(string pth, int rf = 0) {
			name = Path.GetFileName(pth);
			path = pth;
			sourceReference = rf;
		}
	}

	public class Breakpoint
	{
		public bool verified { get; set; }
		public int line { get; set; }

		public Breakpoint(bool v, int l) {
			verified = v;
			line = l;
		}
	}
}
