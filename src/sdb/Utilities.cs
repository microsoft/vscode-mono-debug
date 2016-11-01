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
using System.IO;
using System.Linq;
using Mono.Debugging.Client;

namespace Mono.Debugger.Client
{
    public static class Utilities
    {
        public static bool IsWindows
        {
            get { return (int)Environment.OSVersion.Platform < (int)PlatformID.Unix; }
        }

        public static void Discard<T>(this T value)
        {
        }

        public static string StringizeFrame(StackFrame frame, bool includeIndex)
        {
            var loc = string.Empty;
            string src = null;

            if (frame.SourceLocation.FileName != null)
            {
                loc = " at " + frame.SourceLocation.FileName;

                if (frame.SourceLocation.Line != -1)
                {
                    loc += ":" + frame.SourceLocation.Line;

                    // If the user prefers disassembly, don't
                    // even try to read the source code.
                    if (!Configuration.Current.PreferDisassembly)
                    {
                        StreamReader reader = null;

                        try
                        {
                            reader = File.OpenText(frame.SourceLocation.FileName);

                            var cur = 1;

                            while (!reader.EndOfStream)
                            {
                                var str = reader.ReadLine();

                                if (cur == frame.SourceLocation.Line)
                                {
                                    src = str;
                                    break;
                                }

                                cur++;
                            }
                        }
                        catch (Exception)
                        {
                        }
                        finally
                        {
                            if (reader != null)
                                reader.Dispose();
                        }
                    }
                }
            }

            var tag = string.Empty;

            if (src == null)
            {
                var line = frame.Disassemble(0, 1).FirstOrDefault();

                if (line != null && !line.IsOutOfRange)
                {
                    src = string.Format("    {0}", line.Code);
                    tag = " (no source)";
                }
            }

            var idx = includeIndex ? string.Format("#{0} ", frame.Index) : string.Empty;
            var srcStr = src != null ? Environment.NewLine + src : string.Empty;

            return string.Format("{0}[0x{1:X8}] {2}{3}{4}{5}", idx, frame.Address,
                                 frame.SourceLocation.MethodName, loc, tag, srcStr);
        }

        public static string StringizeThread(ThreadInfo thread, bool includeFrame)
        {
            var f = includeFrame ? thread.Backtrace.GetFrame(0) : null;

            var fstr = f == null ? string.Empty : Environment.NewLine + StringizeFrame(f, false);
            var tstr = string.Format("Thread #{0} '{1}'", thread.Id, thread.Name);

            return string.Format("{0}{1}", tstr, fstr);
        }

        public static Tuple<string, bool> StringizeValue(ObjectValue value)
        {
            string str;
            bool err;

            if (value.IsError)
            {
                str = value.DisplayValue;
                err = true;
            }
            else if (value.IsUnknown)
            {
                str = "Result is unrepresentable";
                err = true;
            }
            else
            {
                str = value.DisplayValue;
                err = false;
            }

            if (Configuration.Current.DebugLogging)
                str += string.Format(" ({0})", value.Flags);

            return Tuple.Create(str, err);
        }
    }
}
