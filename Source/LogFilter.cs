// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using UnityDev.Utils.Configs;
using UnityEngine;

namespace UnityDev.LogConsole {

/// <summary>Keeps and controls filters to apply to the incoming logs.</summary>
static class LogFilter {
  #region Settings
  [SuppressMessage("ReSharper", "InconsistentNaming")]
  class ModuleConfig : PersistentNode {
    /// <summary>Sources that exactly matches the filter will be ignored.</summary>
    public readonly HashSet<string> exactMatch = new();

    /// <summary>Sources that starts from any of the strings in the filter will be ignored.</summary>
    /// <remarks>
    /// Walking through this filter requires full scan (in a worst case) so, it should be of a reasonable size.
    /// </remarks>
    public readonly List<string> prefixMatch = new();
  }
  static readonly ModuleConfig Settings = new();

  static string SilencesFileName => Path.Combine(PluginLoader.PluginRootFolder, "silences.cfg");
  const string ConfigKeyName = "Silences";

  internal static void LoadSettings() {
    var node = SimpleTextSerializer.LoadFromFile(SilencesFileName, ignoreMissing: true)?.GetNode(ConfigKeyName);
    if (node != null) {
      Settings.LoadFromConfigNode(node);
    }
  }

  static void SaveSettings() {
    var wrapperNode = new ConfigNode();
    var node = Settings.GetConfigNode();
    wrapperNode.SetNode(ConfigKeyName, node);
    SimpleTextSerializer.SaveToFile(SilencesFileName, wrapperNode);
  }
  #endregion

  /// <summary>Adds a new filter by exact match of the source.</summary>
  public static void AddSilenceBySource(string source) {
    if (!Settings.exactMatch.Contains(source)) {
      Settings.exactMatch.Add(source);
      Debug.LogWarningFormat("Added exact match silence: {0}", source);
      SaveSettings();
    }
  }

  /// <summary>Adds a new filter by prefix match of the source.</summary>
  public static void AddSilenceByPrefix(string prefix) {
    if (!Settings.prefixMatch.Contains(prefix)) {
      Settings.prefixMatch.Add(prefix);
      Debug.LogWarningFormat("Added prefix match silence: {0}", prefix);
      SaveSettings();
    }
  }
  
  /// <summary>Verifies if <paramref name="log"/> matches the filters.</summary>
  /// <param name="log">A log record to check.</param>
  /// <returns><c>true</c> if any of the filters matched.</returns>
  public static bool CheckLogForFilter(LogInterceptor.Log log) {
    return Settings.exactMatch.Contains(log.Source) || Settings.prefixMatch.Any(log.Source.StartsWith);
  }
}

} // namespace KSPDev
