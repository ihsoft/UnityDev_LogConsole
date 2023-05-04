// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using UnityDev.LogUtils;
using UnityDev.Utils.FSUtils;
using UnityEngine;

namespace UnityDev.LogConsole {

/// <summary>The plugin's bootstrap module.</summary>
/// <remarks>
/// Call the loader as soon as possible in the game. Different applications use different ways of installing plugins, so
/// the actual entry point depends on the specific implementation.
/// </remarks>
static class PluginLoader {
  static bool _isLoadedAndAttached;
  static GameObject _currentGameObject;

  internal static string SessionFileName => ModPaths.MakeAbsPathForPlugin(typeof(PluginLoader), "session.cfg");
  internal static string SettingsFileName => ModPaths.MakeAbsPathForPlugin(typeof(PluginLoader), "settings.cfg");
  internal static string PluginRootFolder => ModPaths.MakeAbsPathForPlugin(typeof(PluginLoader));

  #region Log aggregators
  static readonly PersistentLogAggregator DiskLogAggregator = new();
  internal static readonly PlainLogAggregator RawLogAggregator = new();
  internal static readonly CollapseLogAggregator CollapseLogAggregator = new();
  internal static readonly SmartLogAggregator SmartLogAggregator = new();
  #endregion

  public static void Start(GameObject rootObject) {
    if (_isLoadedAndAttached && _currentGameObject != null && _currentGameObject != rootObject) {
      DebugEx.Info("Skipping re-attach. Attached to: {0}", _currentGameObject);
      return;  // No need to re-attach.
    }

    // Start all aggregators and begin intercepting if not yet done.
    if (!_isLoadedAndAttached) {
      DebugEx.Info("First time attaching LogConsole to: {0}", rootObject);
      _isLoadedAndAttached = true;

      LogInterceptor.LoadSettings();
      LogFilter.LoadSettings();

      DiskLogAggregator.LoadSettings();
      RawLogAggregator.LoadSettings();
      CollapseLogAggregator.LoadSettings();
      SmartLogAggregator.LoadSettings();

      LogInterceptor.StartIntercepting();
      DiskLogAggregator.StartCapture();
      RawLogAggregator.StartCapture();
      CollapseLogAggregator.StartCapture();
      SmartLogAggregator.StartCapture();
    } else {
      DebugEx.Info("Re-Attaching LogConsole to: {0}", rootObject);
    }

    // Attach UI.
    if (_currentGameObject != null) {
      Object.DestroyImmediate(_currentGameObject.GetComponent<PersistentLogAggregatorFlusher>());
      Object.DestroyImmediate(_currentGameObject.GetComponent<ConsoleUI>());
    }
    _currentGameObject = rootObject;
    rootObject.AddComponent<PersistentLogAggregatorFlusher>();
    rootObject.AddComponent<ConsoleUI>();
  }

  /// <summary>Notifies all aggregators tha they need to update the filtering settings.</summary>
  internal static void UpdateAggregatorsConfig() {
    RawLogAggregator.UpdateFilter();
    CollapseLogAggregator.UpdateFilter();
    SmartLogAggregator.UpdateFilter();
    DiskLogAggregator.UpdateFilter();
  }
}

}
