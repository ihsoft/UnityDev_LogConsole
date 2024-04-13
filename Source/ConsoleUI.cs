// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using UnityDev.Utils.Configs;
using UnityDev.Utils.GUIUtils;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace UnityDev.LogConsole {

/// <summary>A console to display Unity's debug logs in-game.</summary>
sealed class ConsoleUI : MonoBehaviour {
  #region Session settings
  [SuppressMessage("ReSharper", "InconsistentNaming")]
  class ModuleSession : PersistentNode {
    public bool showInfo;
    public bool showWarning = true;
    public bool showError = true;
    public bool showException = true;
    public ShowMode logShowMode = ShowMode.Smart;
    public string quickFilterStr = "";
  }
  readonly ModuleSession _session = new();
  #endregion  

  #region Settings
  [SuppressMessage("ReSharper", "InconsistentNaming")]
  [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
  class ModuleConfig : PersistentNode {
    public KeyCode consoleToggleKey = KeyCode.BackQuote;
    public Color infoLogColor = Color.white;
    public Color warningLogColor = Color.yellow;
    public Color errorLogColor = Color.red;
    public Color exceptionLogColor = Color.magenta;
  }
  readonly ModuleConfig _settings = new();
  const string ConfigKeyName = "UI";

  void LoadSettings() {
    var moduleConfig = SimpleTextSerializer.LoadFromFile(PluginLoader.SettingsFileName, ignoreMissing: true)
        ?.GetNode(ConfigKeyName);
    if (moduleConfig != null) {
      _settings.LoadFromConfigNode(moduleConfig);
    }
    var sessionConfig = SimpleTextSerializer.LoadFromFile(PluginLoader.SessionFileName, ignoreMissing: true)
        ?.GetNode(ConfigKeyName);
    if (sessionConfig != null) {
      _session.LoadFromConfigNode(sessionConfig);
    }
  }

  void SaveSettings() {
    var node = _session.GetConfigNode(ConfigKeyName);
    var wrapperNode = new ConfigNode();
    wrapperNode.SetNode(node.Name, node);
    SimpleTextSerializer.SaveToFile(PluginLoader.SessionFileName, wrapperNode);
  }
  #endregion

  #region UI constants
  /// <summary>Console window margin on the screen.</summary>
  const int Margin = 20;

  /// <summary>For every UI window Unity needs a unique ID. This is the one.</summary>
  const int WindowId = 19450509;

  /// <summary>A title bar location.</summary>
  static readonly Rect TitleBarRect = new(0, 0, 10000, 20);

  /// <summary>Style to draw a control of the minimum size.</summary>
  static readonly GUILayoutOption MinSizeLayout = GUILayout.ExpandWidth(false);

  /// <summary>Mode names.</summary>
  static readonly string[] LOGShowingModes = { "Raw", "Collapsed", "Smart" };

  /// <summary>Actual screen position of the console window.</summary>
  Rect _windowRect;

  /// <summary>Box style ot use to present a single record.</summary>
  /// <remarks>It's re-populated on each GUI update call. See <see cref="OnGUI"/>.</remarks>
  GUIStyle _logRecordStyle;
  #endregion

  /// <summary>Display mode constants. Must match <see cref="ConsoleUI.LOGShowingModes"/>.</summary>
  enum ShowMode {
    /// <summary>Simple list of log records.</summary>
    Raw = 0,
    /// <summary>List where identical consecutive records are grouped.</summary>
    Collapsed = 1,
    /// <summary>
    /// List where identical records are grouped globally. If group get updated with a new log record then its timestamp
    /// is updated.
    /// </summary>
    Smart = 2
  }

  /// <summary>A logger that always show a static snapshot.</summary>
  static readonly SnapshotLogAggregator SnapshotLogAggregator = new();

  /// <summary>Log scroll box position.</summary>
  static Vector2 _scrollPosition;

  /// <summary>Specifies if the debug console is visible.</summary>
  static bool _isConsoleVisible;

  /// <summary>ID of the currently selected log record.</summary>
  /// <remarks>It shows expanded.</remarks>
  static int _selectedLogRecordId = -1;

  /// <summary>Indicates that the visible log records should be queried from a
  /// <see cref="SnapshotLogAggregator"/>.</summary>
  static bool _logUpdateIsPaused;

  /// <summary>Indicates that the logs from the current aggregator need to be re-queried.</summary>
  static bool _logsViewChanged;

  /// <summary>A snapshot of the logs for the current view.</summary>
  static IEnumerable<LogRecord> _logsToShow = Array.Empty<LogRecord>();

  /// <summary>Number of the INFO records in the <see cref="_logsToShow"/> collection.</summary>
  static int _infoLogs;
  /// <summary>Number of the WARNING records in the <see cref="_logsToShow"/> collection.</summary>
  static int _warningLogs;
  /// <summary>Number of the ERROR records in the <see cref="_logsToShow"/> collection.</summary>
  static int _errorLogs;
  /// <summary>Number of the EXCEPTION records in the <see cref="_logsToShow"/> collection.</summary>
  static int _exceptionLogs;

  /// <summary>A list of actions to apply at the end of the GUI frame.</summary>
  static readonly GuiActionsList GuiActions = new GuiActionsList();

  /// <summary>Tells if the controls should be shown at the bottom of the dialog.</summary>
  bool _isToolbarAtTheBottom = true;

  /// <summary>Tells if UI was resized since the mod instantiation.</summary>
  bool _initialSizeSet;

  #region Quick filter fields
  /// <summary>Tells if the quick filter editing is active.</summary>
  /// <remarks>Log console update is frozen until the mode is ended.</remarks>
  static bool _quickFilterInputEnabled;

  /// <summary>Tells the last known quick filter status.</summary>
  /// <remarks>It's updated in every <c>OnGUI</c> call. Used to detect the mode change.</remarks>
  static bool _oldQuickFilterInputEnabled;

  /// <summary>The old value of the quick filter before the edit mode has started.</summary>
  static string _oldQuickFilterStr;

  /// <summary>The size for the quick filter input field.</summary>
  static readonly GUILayoutOption QuickFilterSizeLayout = GUILayout.Width(100);
  #endregion

  #region Session persistence

  /// <summary>Only loads the session settings.</summary>
  void Awake() {
    LoadSettings();
  }
  
  /// <summary>Only stores the session settings.</summary>
  void OnDestroy() {
    SaveSettings();
  }

  #endregion

  #region GUI chain

  /// <summary>Actually renders the console window.</summary>
  void OnGUI() {
    if (Event.current.type == EventType.KeyDown && Event.current.keyCode == _settings.consoleToggleKey) {
      _isConsoleVisible = !_isConsoleVisible;
      Event.current.Use();
    }
    if (!_isConsoleVisible) {
      return;
    }

    // Init positioning.
    if (!_initialSizeSet) {
      _initialSizeSet = true;
      ExpandToScreen();
    }

    // Init skin styles.
    _logRecordStyle = new GUIStyle(GUI.skin.box) {
        alignment = TextAnchor.MiddleLeft,
    };
    var title = "UnityDev Logs Console";
    if (!string.IsNullOrEmpty(_session.quickFilterStr)) {
      title += " (filter: <i>" + _session.quickFilterStr + "</i>)";
    }
    if (_logUpdateIsPaused) {
      title += " <i>(PAUSED)</i>";
    }
    _windowRect = GUILayout.Window(WindowId, _windowRect, ConsoleWindowFunc, title);
  }

  /// <summary>Shows a window that displays the recorded logs.</summary>
  /// <param name="windowId">Window ID.</param>
  void ConsoleWindowFunc(int windowId) {
    // Only show the logs snapshot when it's safe to change the GUI layout.
    if (GuiActions.ExecutePendingGuiActions()) {
      UpdateLogsView();
      // Check if the toolbar goes out of the screen.
      _isToolbarAtTheBottom = _windowRect.yMax < Screen.height;
    }

    if (!_isToolbarAtTheBottom) {
      GUICreateToolbar();
    }

    // Main scrolling view.
    using (var logsScrollView = new GUILayout.ScrollViewScope(_scrollPosition)) {
      _scrollPosition = logsScrollView.scrollPosition;

      // Report conditions.
      if (!LogInterceptor.IsStarted) {
        using (new GuiColorScope(contentColor: _settings.errorLogColor)) {
          GUILayout.Label(
              "LogConsole is not handling system logs. Open standard in-game debug console to see the current logs");
        }
      }
      if (_quickFilterInputEnabled) {
        using (new GuiColorScope(contentColor: Color.gray)) {
          GUILayout.Label("<i>Logs update is PAUSED due to the quick filter editing is active. Hit ENTER to accept the"
                          + " filter, or ESC to discard.</i>");
        }
      }

      GUIShowLogRecords();
    }

    if (_isToolbarAtTheBottom) {
      GUICreateToolbar();
    }

    // Allow the window to be dragged by its title bar.
    GuiWindow.DragWindow(ref _windowRect, TitleBarRect);
  }

  /// <summary>Shows the records from the the currently selected aggregator.</summary>
  void GUIShowLogRecords() {
    var capturedRecords = _logsToShow.Where(LogLevelFilter).ToList();
    var showRecords = capturedRecords.Where(LogQuickFilter).ToList();

    // Warn if there are now records to show.
    if (!_quickFilterInputEnabled && !showRecords.Any()) {
      var msg = "No records available for the selected levels";
      if (capturedRecords.Any()) {
        msg += " and quick filter \"" + _session.quickFilterStr + "\"";
      }
      using (new GuiColorScope(contentColor: Color.gray)) {
        GUILayout.Label(msg);
      }
    }

    // Dump the records.
    foreach (var log in showRecords) {
      using (new GuiColorScope(contentColor: GetLogTypeColor(log.SrcLog.Type))) {
        var recordMsg = log.MakeTitle() + (_selectedLogRecordId == log.SrcLog.Id ? ":\n" + log.SrcLog.StackTrace : "");
        GUILayout.Box(recordMsg, _logRecordStyle);

        // Check if log record is selected.
        if (Event.current.type == EventType.MouseDown) {
          if (GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) {
            // Toggle selection.
            var newSelectedId = _selectedLogRecordId == log.SrcLog.Id ? -1 : log.SrcLog.Id;
            GuiActions.Add(() => GuiActionSelectLog(newSelectedId));
          }
        }
      }

      // Present log record details when it's selected.
      if (_selectedLogRecordId == log.SrcLog.Id && log.SrcLog.Source.Any()) {
        GUICreateLogRecordControls(log);
      }
    }
  }

  /// <summary>Displays log records details and creates the relevant controls.</summary>
  /// <param name="log">The selected log record.</param>
  static void GUICreateLogRecordControls(LogRecord log) {
    using (new GUILayout.HorizontalScope()) {
      // Add stack trace utils.
      using (new GuiEnabledStateScope(!log.SrcLog.FilenamesResolved)) {
        if (GUILayout.Button("Resolve file names", MinSizeLayout)) {
          log.ResolveStackFilenames();
        }
      }

      // Add source and filter controls when expanded.
      GUILayout.Label("Silence: source", MinSizeLayout);
      if (GUILayout.Button(log.SrcLog.Source, MinSizeLayout)) {
        GuiActions.Add(() => GuiActionAddSilence(log.SrcLog.Source, isPrefix: false));
      }
      var sourceParts = log.SrcLog.Source.Split('.');
      if (sourceParts.Length > 1) {
        GUILayout.Label("or by prefix", MinSizeLayout);
        for (var i = sourceParts.Length - 1; i > 0; --i) {
          var prefix = string.Join(".", sourceParts.Take(i).ToArray()) + '.';
          if (GUILayout.Button(prefix, MinSizeLayout)) {
            GuiActions.Add(() => GuiActionAddSilence(prefix, isPrefix: true));
          }
        }
      }
    }
  }

  /// <summary>Creates controls for the console.</summary>
  void GUICreateToolbar() {
    using (new GUILayout.HorizontalScope()) {
      // Window size/snap.
      if (GUILayout.Button("Expand", MinSizeLayout)) {
        ExpandToScreen();
      }
      if (GUILayout.Button("Up", MinSizeLayout)) {
        SnapToTop();
      }
      if (GUILayout.Button("Down", MinSizeLayout)) {
        SnapToBottom();
      }

      // Quick filter.
      // Due to Unity GUI behavior, any change to the layout resets the text field focus. We do some tricks here to set
      // initial focus to the field but not locking it permanently.
      GUILayout.Label("Quick filter:", MinSizeLayout);
      if (_quickFilterInputEnabled) {
        GUI.SetNextControlName("quickFilter");
        _session.quickFilterStr = GUILayout.TextField(_session.quickFilterStr, QuickFilterSizeLayout);
        if (Event.current.type == EventType.KeyUp) {
          if (Event.current.keyCode == KeyCode.Return) {
            GuiActions.Add(GuiActionAcceptQuickFilter);
          } else if (Event.current.keyCode == KeyCode.Escape) {
            GuiActions.Add(GuiActionCancelQuickFilter);
          }
        } else if (Event.current.type == EventType.Layout && GUI.GetNameOfFocusedControl() != "quickFilter") {
          if (_oldQuickFilterInputEnabled != _quickFilterInputEnabled && !_oldQuickFilterInputEnabled) {
            GUI.FocusControl("quickFilter");  // Initial set of the focus.
          } else {
            GuiActions.Add(GuiActionCancelQuickFilter);  // The field has lost the focus.
          }
        }  
      } else {
        var title = _session.quickFilterStr == "" ? "<i>NONE</i>" : _session.quickFilterStr;
        if (GUILayout.Button(title, QuickFilterSizeLayout)) {
          GuiActions.Add(GuiActionStartQuickFilter);
        }
      }
      _oldQuickFilterInputEnabled = _quickFilterInputEnabled;

      using (new GuiEnabledStateScope(!_quickFilterInputEnabled)) {
        // Clear logs in the current aggregator.
        if (GUILayout.Button("Clear")) {
          GuiActions.Add(GuiActionClearLogs);
        }

        // Log mode selection. 
        GUI.changed = false;
        var showMode = GUILayout.SelectionGrid(
            (int) _session.logShowMode, LOGShowingModes, LOGShowingModes.Length, MinSizeLayout);
        _logsViewChanged |= GUI.changed;
        if (GUI.changed) {
          GuiActions.Add(() => GuiActionSetMode((ShowMode) showMode));
        }

        // Paused state selection.
        GUI.changed = false;
        var isPaused = GUILayout.Toggle(_logUpdateIsPaused, "PAUSED", MinSizeLayout);
        if (GUI.changed) {
          GuiActions.Add(() => GuiActionSetPaused(isPaused));
        }
        
        // Draw logs filter by level and refresh logs when filter changes.
        GUI.changed = false;
        using (new GuiColorScope()) {
          GUI.contentColor = _settings.infoLogColor;
          _session.showInfo = GUILayout.Toggle(_session.showInfo, $"INFO ({_infoLogs})", MinSizeLayout);
          GUI.contentColor = _settings.warningLogColor;
          _session.showWarning = GUILayout.Toggle(_session.showWarning, $"WARNING ({_warningLogs})", MinSizeLayout);
          GUI.contentColor = _settings.errorLogColor;
          _session.showError = GUILayout.Toggle(_session.showError, $"ERROR ({_errorLogs})", MinSizeLayout);
          GUI.contentColor = _settings.exceptionLogColor;
          _session.showException = GUILayout.Toggle(_session.showException, $"EXCEPTION ({_exceptionLogs})", MinSizeLayout);
        }
        _logsViewChanged |= GUI.changed;
      }
    }
  }

  /// <summary>Verifies if level of the log record is needed by the UI.</summary>
  /// <param name="log">The log record to verify.</param>
  /// <returns><c>true</c> if this level is visible.</returns>
  bool LogLevelFilter(LogRecord log) {
    return log.SrcLog.Type == LogType.Exception && _session.showException
        || log.SrcLog.Type == LogType.Error && _session.showError
        || log.SrcLog.Type == LogType.Warning && _session.showWarning
        || log.SrcLog.Type == LogType.Log && _session.showInfo;
  }

  /// <summary>Gives a color for the requested log type.</summary>
  /// <param name="type">A log type to get color for.</param>
  /// <returns>A color for the type.</returns>
  Color GetLogTypeColor(LogType type) {
    return type switch {
        LogType.Log => _settings.infoLogColor,
        LogType.Warning => _settings.warningLogColor,
        LogType.Error => _settings.errorLogColor,
        LogType.Exception => _settings.exceptionLogColor,
        _ => Color.gray
    };
  }

  /// <summary>Verifies if the log record matches the quick filter criteria.</summary>
  /// <remarks>The quick filter string is a case-insensitive prefix of the log's source.</remarks>
  /// <param name="log">The log record to verify.</param>
  /// <returns><c>true</c> if this log passes the quick filter check.</returns>
  bool LogQuickFilter(LogRecord log) {
    var filter = _quickFilterInputEnabled ? _oldQuickFilterStr : _session.quickFilterStr;
    return log.SrcLog.Source.StartsWith(filter, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>Populates <see cref="_logsToShow"/> and the stats numbers.</summary>
  /// <remarks>
  /// The current aggregator is determined from <see cref="ModuleSession.logShowMode"/> and
  /// <see cref="_logUpdateIsPaused"/> state.
  /// </remarks>
  void UpdateLogsView() {
    var currentAggregator = _logUpdateIsPaused ? SnapshotLogAggregator : GetCurrentAggregator();
    if (currentAggregator.FlushBufferedLogs() || _logsViewChanged) {
      _logsToShow = currentAggregator.GetLogRecords();
      _infoLogs = currentAggregator.InfoLogsCount;
      _warningLogs = currentAggregator.WarningLogsCount;
      _errorLogs = currentAggregator.ErrorLogsCount;
      _exceptionLogs = currentAggregator.ExceptionLogsCount;
    }
    _logsViewChanged = false;
  }
  
  /// <summary>Returns an aggregator for the currently selected mode.</summary>
  /// <returns>An aggregator.</returns>
  BaseLogAggregator GetCurrentAggregator() {
    return _session.logShowMode switch {
        ShowMode.Raw => PluginLoader.RawLogAggregator,
        ShowMode.Collapsed => PluginLoader.CollapseLogAggregator,
        ShowMode.Smart => PluginLoader.SmartLogAggregator,
        _ => PluginLoader.RawLogAggregator
    };
  }

  #endregion

  #region GUI snap methods

  void SnapToTop() {
    _windowRect = new Rect(Margin, Margin, Screen.width - Margin * 2, (Screen.height - Margin * 2.0f) / 3);
  }

  void SnapToBottom() {
    var clientHeight = (Screen.height - 2.0f * Margin) / 3;
    _windowRect = new Rect(Margin, Screen.height - Margin - clientHeight, Screen.width - Margin * 2, clientHeight);
  }

  void ExpandToScreen() {
    _windowRect = new Rect(Margin, Margin, Screen.width - Margin * 2, Screen.height - Margin * 2);
  }

  #endregion

  #region GUI action handlers

  void GuiActionSetPaused(bool isPaused) {
    if (isPaused == _logUpdateIsPaused) {
      return;  // Prevent refreshing of the snapshot if the mode hasn't changed.
    }
    if (isPaused) {
      SnapshotLogAggregator.LoadLogs(GetCurrentAggregator());
    }
    _logUpdateIsPaused = isPaused;
    _logsViewChanged = true;
  }

  void GuiActionCancelQuickFilter() {
    if (_quickFilterInputEnabled) {
      _quickFilterInputEnabled = false;
      _session.quickFilterStr = _oldQuickFilterStr;
      _oldQuickFilterStr = null;
      GuiActionSetPaused(false);
    }
  }

  void GuiActionAcceptQuickFilter() {
    _quickFilterInputEnabled = false;
    _oldQuickFilterStr = null;
    GuiActionSetPaused(false);
  }

  void GuiActionStartQuickFilter() {
    _quickFilterInputEnabled = true;
    _oldQuickFilterStr = _session.quickFilterStr;
    GuiActionSetPaused(true);
  }

  void GuiActionClearLogs() {
    GuiActionSetPaused(false);
    GetCurrentAggregator().ClearAllLogs();
    _logsViewChanged = true;
  }

  static void GuiActionSelectLog(int newSelectedId) {
    _selectedLogRecordId = newSelectedId;
  }

  static void GuiActionAddSilence(string pattern, bool isPrefix) {
    if (isPrefix) {
      LogFilter.AddSilenceByPrefix(pattern);
    } else {
      LogFilter.AddSilenceBySource(pattern);
    }

    PluginLoader.UpdateAggregatorsConfig();
    SnapshotLogAggregator.UpdateFilter();
    _logsViewChanged = true;
  }
  
  void GuiActionSetMode(ShowMode mode) {
    _session.logShowMode = mode;
    GuiActionSetPaused(false);  // New mode invalidates the snapshot.
    _logsViewChanged = true;
  }

  #endregion
}

}
