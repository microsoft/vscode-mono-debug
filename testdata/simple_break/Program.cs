using System;
using System.Diagnostics;

namespace Tests
{
	class Simple {

		public static void Main(string[] args) {

			if (System.Diagnostics.Debugger.IsAttached) {
				Debugger.Break();
			}

			Console.WriteLine("Hello World!");

			Console.WriteLine("The End.");
		}
	}
}
