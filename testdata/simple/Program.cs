using System;
using System.Diagnostics;
using System.Threading;

namespace Tests
{
	class Simple {

		public static void Main(string[] args) {

			Thread.Sleep(100);	// wait a bit so that debugger gets enough time to set breakpoints

			Console.WriteLine("Hello World!");

			Console.WriteLine("The End.");
		}
	}
}
