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
using System.Linq;

namespace Mono.Debugger.Client
{
    public static class Color
    {
        // We need this class because the color sequences emitted by
        // the ForegroundColor, BackgroundColor, and ResetColor helpers
        // on System.Console mess with libedit's input and history
        // tracker.

        static readonly bool _disableColors;

        static Color()
        {
            _disableColors = Console.IsOutputRedirected ||
                             new[] { null, "dumb" }.Contains(Environment.GetEnvironmentVariable("TERM")) ||
                             Environment.GetEnvironmentVariable("SDB_COLORS") == "disable";
        }

        static string GetColor(string modifier, string color)
        {
            return _disableColors || Configuration.Current.DisableColors ?
                   string.Empty : modifier + color;
        }

        public static string Red
        {
            get { return GetColor("\x1b[1m", "\x1b[31m"); }
        }

        public static string DarkRed
        {
            get { return GetColor(string.Empty, "\x1b[31m"); }
        }

        public static string Green
        {
            get { return GetColor("\x1b[1m", "\x1b[32m"); }
        }

        public static string DarkGreen
        {
            get { return GetColor(string.Empty, "\x1b[32m"); }
        }

        public static string Yellow
        {
            get { return GetColor("\x1b[1m", "\x1b[33m"); }
        }

        public static string DarkYellow
        {
            get { return GetColor(string.Empty, "\x1b[33m"); }
        }

        public static string Blue
        {
            get { return GetColor("\x1b[1m", "\x1b[34m"); }
        }

        public static string DarkBlue
        {
            get { return GetColor(string.Empty, "\x1b[34m"); }
        }

        public static string Magenta
        {
            get { return GetColor("\x1b[1m", "\x1b[35m"); }
        }

        public static string DarkMagenta
        {
            get { return GetColor(string.Empty, "\x1b[35m"); }
        }

        public static string Cyan
        {
            get { return GetColor("\x1b[1m", "\x1b[36m"); }
        }

        public static string DarkCyan
        {
            get { return GetColor(string.Empty, "\x1b[36m"); }
        }

        public static string Reset
        {
            get { return GetColor("\x1b[0m", string.Empty); }
        }
    }
}
