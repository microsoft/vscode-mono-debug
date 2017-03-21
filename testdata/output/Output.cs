using System;
using System.Diagnostics;

namespace Simple
{
	class Simple {
		public static void Main(string[] args) {
			for (int i = 0; i < 3; i++) {
				Console.Error.WriteLine("Hello stderr " + i);
			}
			for (int i = 0; i < 3; i++) {
				Console.WriteLine("Hello stdout " + i);
			}
		}
	}
}
