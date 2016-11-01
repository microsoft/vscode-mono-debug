//
// The MIT License (MIT)
//
// Copyright (c) 2015 Alex Rønne Petersen
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

using System;

namespace Mono.Debugger.Client
{
    public static class Log
    {
        static readonly bool _debug = Environment.GetEnvironmentVariable("SDB_DEBUG") == "enable";

        public static object Lock { get; private set; }

        static Log()
        {
            Lock = new object();
        }

        static void Output(bool nl, string color, string format, object[] args)
        {
            var str = color + (args.Length == 0 ? format : string.Format(format, args)) + Color.Reset;

            lock (Lock)
            {
                if (nl)
                    Console.WriteLine(str);
                else
                    Console.Write(str);
            }
        }

        public static void InfoSameLine(string format, params object[] args)
        {
            Output(false, string.Empty, format, args);
        }

        public static void Info(string format, params object[] args)
        {
            Output(true, string.Empty, format, args);
        }

        public static void NoticeSameLine(string format, params object[] args)
        {
            Output(false, Color.DarkCyan, format, args);
        }

        public static void Notice(string format, params object[] args)
        {
            Output(true, Color.DarkCyan, format, args);
        }

        public static void EmphasisSameLine(string format, params object[] args)
        {
            Output(false, Color.DarkGreen, format, args);
        }

        public static void Emphasis(string format, params object[] args)
        {
            Output(true, Color.DarkGreen, format, args);
        }

        public static void ErrorSameLine(string format, params object[] args)
        {
            Output(false, Color.DarkRed, format, args);
        }

        public static void Error(string format, params object[] args)
        {
            Output(true, Color.DarkRed, format, args);
        }

        public static void DebugSameLine(string format, params object[] args)
        {
            if (_debug || Configuration.Current.DebugLogging)
                Output(false, Color.DarkYellow, format, args);
        }

        public static void Debug(string format, params object[] args)
        {
            if (_debug || Configuration.Current.DebugLogging)
                Output(true, Color.DarkYellow, format, args);
        }
    }
}
