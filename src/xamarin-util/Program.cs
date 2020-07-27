using Mono.Options;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Text.Json;

namespace VsCodeXamarinUtil
{
	class Program
	{
		const string helpCommand = "help";

		static void Main(string[] args)
		{
			var options = new OptionSet();

			var command = helpCommand;
			var id = Guid.NewGuid().ToString();

			options.Add("c|command=", "get the tool version", s => command = s?.ToLowerInvariant()?.Trim() ?? helpCommand);
			options.Add("h|help", "prints the help", s => command = helpCommand);
			options.Add("i|id=", "unique identifier of the command", s => id = s);

			var extras = options.Parse(args);

			if (command.Equals(helpCommand))
			{
				ShowHelp(options);
				return;
			}

			var response = new CommandResponse
			{
				Id = id,
				Command = command
			};

			object responseObject = null;

			try
			{
				responseObject = command switch
				{
					"version" => Version(),
					"android-devices" => AndroidDevices(),
					_ => Version()
				};
			}
			catch (Exception ex)
			{
				response.Error = ex.Message;
			}

			response.Response = responseObject;

			var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
			{
				IgnoreNullValues = true,
				PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
				DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
				WriteIndented = true
			});
			
			Console.WriteLine(json);
		}

		static void ShowHelp(OptionSet p)
		{
			Console.WriteLine("Usage: vscode-xamarin-util [OPTIONS]+");
			Console.WriteLine();
			Console.WriteLine("Options:");
			p.WriteOptionDescriptions(Console.Out);
		}

		static object Version()
			=> new { Version = "0.1.1" };

		static DirectoryInfo GetAndroidSdkHome()
		{
			var sdkHome = AndroidSdk.FindHome();

			if (sdkHome == null)
				throw new Exception("Android SDK Not Found");

			return sdkHome;
		}

		static IEnumerable<DeviceData> AndroidDevices()
			=> AndroidSdk.GetEmulatorsAndDevices(GetAndroidSdkHome());
	}
}
