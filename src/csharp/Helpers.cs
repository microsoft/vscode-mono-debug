using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics;
using System.Xml;
using System.Threading.Tasks;
using System.Threading;

namespace VSCodeDebug
{
  enum McNeelProjectType
  {
    None,
    DebugStarter,
    RhinoCommon,
    Grasshopper
  }

  static class Helpers
  {
    public const string StandardInstallPath = "/Applications/Rhinoceros.app";
    public const string StandardInstallWipPath = "/Applications/RhinoWIP.app";
    public const string StandardInstallBetaPath = "/Applications/RhinoBETA.app";

    public static string GetExecutablePath(string applicationPath)
    {
      if (string.IsNullOrEmpty(applicationPath))
        return null;

      var executablePath = Path.Combine(applicationPath, "Contents", "MacOS", "Rhinoceros");
      if (File.Exists(executablePath))
        return executablePath;

      // old versions of v5 use this executable
      executablePath = Path.Combine(applicationPath, "Contents", "MacOS", "Rhino");
      if (File.Exists(executablePath))
        return executablePath;

      return null;
    }


    public static string GetXcodeDerivedDataPath(string targetDirectory)
    {
      var homePath = Environment.GetEnvironmentVariable("HOME");
      var derivedDataPath = Path.Combine(homePath, "Library", "Developer", "Xcode", "DerivedData");
      if (!Directory.Exists(derivedDataPath))
        return null;

      var dataPaths = Directory.GetDirectories(derivedDataPath).Where(r => Path.GetFileName(r).StartsWith("MacRhino-", StringComparison.Ordinal));
      foreach (var dataPath in dataPaths)
      {
        if (dataPath == null)
          continue;

        // load up info.plist to get WorkspacePath and compare with our target directory
        try
        {
          var infoPath = Path.Combine(dataPath, "info.plist");
          var doc = new XmlDocument();
          doc.Load(infoPath);
          var workspacePath = doc.SelectSingleNode("plist/dict/key[.='WorkspacePath']/following-sibling::string[1]")?.InnerText;

          var workspaceDir = new DirectoryInfo(workspacePath);
          if (!workspaceDir.Exists)
            continue;

          var commonPath = FindCommonPath(Path.DirectorySeparatorChar, new[] { workspacePath, targetDirectory });

          if (commonPath == targetDirectory)
            return dataPath;

          /*
          // assume the workspacePath points to MacRhino.xcodeproj, so check based on its parent folders, which should be src4 or the root.
          if (commonPath == workspaceDir.Parent?.Parent?.Parent?.FullName || commonPath == workspaceDir.Parent?.Parent?.FullName)
          {

            var appPath = Path.Combine(dataPath, "Build", "Products", "Debug", "Rhinoceros.app");

            if (Directory.Exists(appPath))
              return appPath;
          }
          */
        }
        catch
        {
          continue;
        }
      }
      return null;
    }

    public static string FindCommonPath(char separator, IEnumerable<string> paths)
    {
      string commonPath = String.Empty;
      var separatedPaths = paths
        .First(str => str.Length == paths.Max(st2 => st2.Length))
        .Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)
        .ToList();

      foreach (string segment in separatedPaths)
      {
        if (commonPath.Length == 0 && paths.All(str => str.StartsWith(segment, StringComparison.Ordinal)))
        {
          commonPath = segment;
        }
        else if (paths.All(str => str.StartsWith(commonPath + separator + segment, StringComparison.Ordinal)))
        {
          commonPath += separator + segment;
        }
        else
        {
          break;
        }
      }

      return commonPath;
    }

  }
}

