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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using Mono.Debugging.Client;

namespace Mono.Debugger.Client
{
    [Serializable]
    public sealed class Configuration
    {
        public bool AllowMethodEvaluation { get; set; }

        public bool AllowTargetInvoke { get; set; }

        public bool AllowToStringCalls { get; set; }

        public bool ChunkRawStrings { get; set; }

        public int ConnectionAttemptInterval { get; set; }

        public string DefaultDatabaseFile { get; set; }

        public bool DebugLogging { get; set; }

        public bool DisableColors { get; set; }

        public bool EllipsizeStrings { get; set; }

        public int EllipsizeThreshold { get; set; }

        public bool EnableControlC { get; set; }

        public int EvaluationTimeout { get; set; }

        public string ExceptionIdentifier { get; set; }

        public bool FlattenHierarchy { get; set; }

        public bool HexadecimalIntegers { get; set; }

        public string InputPrompt { get; set; }

        public bool LoadDatabaseAutomatically { get; set; }

        public bool LogInternalErrors { get; set; }

        public bool LogRuntimeSpew { get; set; }

        public int MaxConnectionAttempts { get; set; }

        public int MemberEvaluationTimeout { get; set; }

        public bool PreferDisassembly { get; set; }

        public string RuntimeExecutable { get; set; }

        public string RuntimePrefix { get; set; }

        public bool SaveDatabaseAutomatically { get; set; }

        public bool StepOverPropertiesAndOperators { get; set; }

        public Dictionary<string, Tuple<TypeCode, object, object>> Extra { get; private set; }

        public static Configuration Current { get; private set; }

        static Configuration()
        {
            Current = new Configuration();
        }

        Configuration()
        {
            Extra = new Dictionary<string, Tuple<TypeCode, object, object>>();
        }

        public void Declare(string name, TypeCode type, object defaultValue)
        {
            if (!Extra.ContainsKey(name))
                Extra.Add(name, Tuple.Create(type, defaultValue, defaultValue));
        }

        static string GetFilePath()
        {
            var cfg = Environment.GetEnvironmentVariable("SDB_CFG");

            if (cfg != null)
                return cfg == string.Empty ? null : cfg;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            return Path.Combine(home, ".sdb.cfg");
        }

        public static void Write()
        {
            var file = GetFilePath();

            if (file == null)
                return;

            try
            {
                using (var stream = new FileStream(file, FileMode.Create, FileAccess.Write))
                    new BinaryFormatter().Serialize(stream, Current);
            }
            catch (Exception ex)
            {
                Log.Error("Could not write configuration file '{0}':", file);
                Log.Error(ex.ToString());
            }
        }

        public static bool Read()
        {
            var file = GetFilePath();

            if (file == null)
                return false;

            try
            {
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read))
                {
                    Current = (Configuration)new BinaryFormatter().Deserialize(stream);

                    // Some logic to fix up older serialized
                    // configuration data follows...

                    if (Current.Extra == null)
                        Current.Extra = new Dictionary<string, Tuple<TypeCode, object, object>>();

                    if (Current.DefaultDatabaseFile == null)
                        Current.DefaultDatabaseFile = string.Empty;

                    if (Current.RuntimeExecutable == null)
                        Current.RuntimeExecutable = string.Empty;
                }
            }
            catch (Exception ex)
            {
                // If it's an FNFE, chances are the file just
                // hasn't been written yet.
                if (!(ex is FileNotFoundException))
                {
                    Log.Error("Could not read configuration file '{0}':", file);
                    Log.Error(ex.ToString());
                }

                return false;
            }

            return true;
        }

        public static void Defaults()
        {
            // Cute hack to set all properties to their default values.
            foreach (var prop in typeof(Configuration).GetProperties(BindingFlags.Public |
                                                                     BindingFlags.Instance))
                if (prop.Name != "Extra")
                    prop.SetValue(Configuration.Current, null);

            Current.AllowMethodEvaluation = true;
            Current.AllowTargetInvoke = true;
            Current.AllowToStringCalls = true;
            Current.ConnectionAttemptInterval = 500;
            Current.DefaultDatabaseFile = string.Empty;
            Current.EllipsizeStrings = true;
            Current.EllipsizeThreshold = 100;
            Current.EnableControlC = true;
            Current.EvaluationTimeout = 1000;
            Current.ExceptionIdentifier = "$exception";
            Current.FlattenHierarchy = true;
            Current.InputPrompt = "(sdb)";
            Current.MaxConnectionAttempts = 1;
            Current.MemberEvaluationTimeout = 5000;
            Current.RuntimePrefix = "/usr";
            Current.RuntimeExecutable = string.Empty;
            Current.StepOverPropertiesAndOperators = true;

            var defs = Current.Extra.Select(kvp => Tuple.Create(kvp.Key, kvp.Value.Item1, kvp.Value.Item2));

            foreach (var def in defs)
                Current.Extra[def.Item1] = Tuple.Create(def.Item2, def.Item3, def.Item3);
        }

        public static void Apply()
        {
            // We can only apply a limited set of options here since some
            // are set at session creation time.

            var opt = Debugger.Options;

            opt.StepOverPropertiesAndOperators = Current.StepOverPropertiesAndOperators;

            var eval = opt.EvaluationOptions;

            eval.AllowMethodEvaluation = Current.AllowMethodEvaluation;
            eval.AllowTargetInvoke = Current.AllowTargetInvoke;
            eval.AllowToStringCalls = Current.AllowToStringCalls;
            eval.ChunkRawStrings = Current.ChunkRawStrings;
            eval.CurrentExceptionTag = Current.ExceptionIdentifier;
            eval.EllipsizeStrings = Current.EllipsizeStrings;
            eval.EllipsizedLength = Current.EllipsizeThreshold;
            eval.EvaluationTimeout = Current.EvaluationTimeout;
            eval.FlattenHierarchy = Current.FlattenHierarchy;
            eval.IntegerDisplayFormat = Current.HexadecimalIntegers ?
                                        IntegerDisplayFormat.Hexadecimal :
                                        IntegerDisplayFormat.Decimal;
            eval.MemberEvaluationTimeout = Current.MemberEvaluationTimeout;

            if (Current.EnableControlC)
                CommandLine.SetControlCHandler();
            else
                CommandLine.UnsetControlCHandler();
        }
    }
}
