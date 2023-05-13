// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public domain license.

using System;
using System.IO;
using UnityDev.Utils.FSUtils;

namespace UnityDev.Utils.LogUtils {

/// <summary>Logging settings.</summary>
/// <remarks>
/// The settings are not loaded pro-actively. The config is supposed to be loaded when it's first time needed.
/// </remarks>
public static class LoggingSettings {

  /// <summary>Level above 0 enables <see cref="DebugEx.Fine"/> logs.</summary>
  public static int VerbosityLevel {
    get {
      if (_verbosityLevel < 0) {
        LoadSettings();
      }
      return _verbosityLevel;
    }
  }
  static int _verbosityLevel = -1;

  static void LoadSettings() {
    var configPath = ModPaths.MakeAbsPathForPlugin(typeof(LoggingSettings), "UnityDev_loglevel.txt");
    if (File.Exists(configPath)) {
      var lines = File.ReadAllLines(configPath);
      if (lines.Length == 0) {
        return;
      }
      var line = lines[0].Trim();
      try {
        var level = int.Parse(lines[0].Trim());
        _verbosityLevel = level > 0 ? level : 0;
        DebugEx.Fine("Loaded UnityDev settings. Logs verbosity: {0}", VerbosityLevel);
      } catch (Exception) {
        DebugEx.Error("Cannot parse verbosity level: {0}", line);
      }
    } else {
      _verbosityLevel = 0;
    }
  }
}
}
