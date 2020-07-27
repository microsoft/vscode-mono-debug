using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VsCodeXamarinUtil
{
	public class AndroidSdk
	{
		static string[] KnownLikelyPaths =>
			new string[] {
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Android", "android-sdk"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android", "android-sdk"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Developer", "android-sdk-macosx"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Developer", "Xamarin", "android-sdk-macosx"),
				Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Developer", "AndroidSdk"),
				Path.Combine("Library", "Developer", "AndroidSdk"),
				Path.Combine("Developer", "AndroidSdk"),
				Path.Combine("Developer", "Android", "android-sdk-macosx"),
			};

		public static DirectoryInfo FindHome(string specificHome = null, params string[] additionalPossibleDirectories)
		{
			var candidates = new List<string>();

			if (specificHome != null)
			{
				candidates.Add(specificHome);
			}
			else
			{
				candidates.Add(Environment.GetEnvironmentVariable("ANDROID_HOME"));
				if (additionalPossibleDirectories != null)
					candidates.AddRange(additionalPossibleDirectories);
				candidates.AddRange(KnownLikelyPaths);
			}

			foreach (var c in candidates)
			{
				if (!string.IsNullOrWhiteSpace(c) && Directory.Exists(c))
					return new DirectoryInfo(c);
			}

			return null;
		}

		static Regex rxWhitespace = new Regex("\\s+", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

		public static List<DeviceData> GetEmulatorsAndDevices(DirectoryInfo sdkHome)
		{
			var devices = new List<DeviceData>();

			var adbTool = GetAdbTool(sdkHome);

			var adbDevicesResult = ProcessRunner.Run(adbTool, new ProcessArgumentBuilder().Append("devices"));

			// Remove the "List of devices attached" line of output
			if (adbDevicesResult.StandardOutput.Any())
				adbDevicesResult.StandardOutput.RemoveAt(0);

			foreach (var line in adbDevicesResult.StandardOutput)
			{
				if (string.IsNullOrWhiteSpace(line))
					continue;

				var lineParts = rxWhitespace.Split(line.Trim());

				var serial = lineParts.FirstOrDefault();

				if (string.IsNullOrWhiteSpace(serial))
					continue;

				var isEmulator = serial.StartsWith("emulator-");
				var isRunning = !isEmulator || (isEmulator && !line.Contains("offline"));

				var name = isRunning ? GetDeviceName(sdkHome, serial) : serial;

				devices.Add(new DeviceData
				{
					Serial = serial,
					IsEmulator = isEmulator,
					IsRunning = !isEmulator || (isEmulator && !line.Contains("offline")),
					Name = name
				});
			}

			return devices;
		}

		static string GetEmulatorName(DirectoryInfo sdkHome, string adbSerial = null)
		{
			var shellName = EmuAvdName(sdkHome, adbSerial);

			if (!string.IsNullOrWhiteSpace(shellName))
				return shellName;

			if (string.IsNullOrEmpty(adbSerial) || !adbSerial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("Serial must be an emulator starting with `emulator-`");

			int port = 5554;
			if (!int.TryParse(adbSerial.Substring(9), out port))
				return null;

			var tcpClient = new System.Net.Sockets.TcpClient("127.0.0.1", port);
			var name = string.Empty;
			using (var s = tcpClient.GetStream())
			{

				System.Threading.Thread.Sleep(250);

				foreach (var b in Encoding.ASCII.GetBytes("avd name\r\n"))
					s.WriteByte(b);

				System.Threading.Thread.Sleep(250);

				byte[] data = new byte[1024];
				using (var memoryStream = new MemoryStream())
				{
					do
					{
						var len = s.Read(data, 0, data.Length);
						memoryStream.Write(data, 0, len);
					} while (s.DataAvailable);

					var txt = Encoding.ASCII.GetString(memoryStream.ToArray(), 0, (int)memoryStream.Length);

					var m = Regex.Match(txt, "OK(?<name>.*?)OK", RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline);
					name = m?.Groups?["name"]?.Value?.Trim();
				}
			}

			return name;
		}

		static string EmuAvdName(DirectoryInfo sdkHome, string adbSerial = null)
		{
			// adb uninstall -k <package>
			// -k keeps data & cache dir
			var builder = new ProcessArgumentBuilder();

			if (!string.IsNullOrEmpty(adbSerial))
			{
				builder.Append("-s");
				builder.AppendQuoted(adbSerial);
			}

			builder.Append("emu");
			builder.Append("avd");
			builder.Append("name");

			var r = ProcessRunner.Run(GetAdbTool(sdkHome), builder);

			return r?.StandardOutput?.FirstOrDefault()?.Trim();
		}

		static string GetDeviceName(DirectoryInfo sdkHome, string adbSerial = null)
		{
			try
			{
				return GetEmulatorName(sdkHome, adbSerial);
			}
			catch (InvalidDataException)
			{
				// Shell getprop
				var s = Shell(sdkHome, "getprop ro.product.model", adbSerial);

				if (s?.Any() ?? false)
					return s.FirstOrDefault().Trim();

				// Shell getprop
				s = Shell(sdkHome, "getprop ro.product.name", adbSerial);

				if (s?.Any() ?? false)
					return s.FirstOrDefault().Trim();
			}

			return null;
		}

		static List<string> Shell(DirectoryInfo sdkHome, string shellCommand, string adbSerial = null)
		{
			// adb uninstall -k <package>
			// -k keeps data & cache dir
			var builder = new ProcessArgumentBuilder();

			if (!string.IsNullOrEmpty(adbSerial))
			{
				builder.Append("-s");
				builder.AppendQuoted(adbSerial);
			}

			builder.Append("shell");
			builder.Append(shellCommand);

			var output = new List<string>();

			var r = ProcessRunner.Run(GetAdbTool(sdkHome), builder);

			return r.StandardOutput;
		}

		static string ExeSuffix => IsWindows ? ".exe" : string.Empty;

		static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

		static FileInfo GetAdbTool(DirectoryInfo sdkHome)
			=> new FileInfo(Path.Combine(sdkHome.FullName, "platform-tools", "adb" + ExeSuffix));
	}
}