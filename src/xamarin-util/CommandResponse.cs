using System;
using System.Collections.Generic;
using System.Text;

namespace VsCodeXamarinUtil
{
	public class CommandResponse
	{
		public CommandResponse()
		{
		}

		public string Id { get; set; }

		public string Command { get; set; }

		public string Error { get; set; }

		public object Response { get; set; }
	}
}
