// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System;
using System.Diagnostics.CodeAnalysis;
using UnityDev.Utils.Configs;
using UnityEngine;

namespace UnityDev.LogConsole {

/// <summary>A log capture that writes logs on disk.</summary>
/// <remarks>
/// <p>Three files are created: <list type="bullet">
/// <item><c>INFO</c> that includes all logs;</item>
/// <item><c>WARNING</c> which captures warnings and errors;</item>
/// <item><c>ERROR</c> for the errors (including exceptions).</item>
/// </list>
/// </p>
/// <p>Persistent logging must be explicitly enabled via <c>PersistentLogs-settings.cfg</c></p>
/// </remarks>
sealed class PersistentLogAggregator : BaseLogAggregator {
  #region Settings
  [SuppressMessage("ReSharper", "InconsistentNaming")]
  new class ModuleConfig  : BaseLogAggregator.ModuleConfig {
    public class CleanupPolicyGroup : PersistentNode {
      /// <summary>Limits total number of log files in the directory.</summary>
      /// <remarks>Only files that match file prefix are counted. Older files will be drop to satisfy the limit.</remarks>
      public int totalFiles = 30;

      /// <summary>Limits total size of all log files in the directory.</summary>
      /// <remarks>Only files that match file prefix are counted. Older files will be drop to satisfy the limit.</remarks>
      public int totalSizeMb = 100;

      /// <summary>Maximum age of the log files in the directory.</summary>
      /// <remarks>Only files that match file prefix are counted. Older files will be drop to satisfy the limit.</remarks>
      public int maxAgeHours = 168;  // 7 days
    }

    /// <summary>Specifies if INFO file should be written.</summary>
    public bool writeInfoFile = true;

    /// <summary>Specifies if WARNING file should be written.</summary>
    public bool writeWarningFile = true;

    /// <summary>Specifies if ERROR file should be written.</summary>
    public bool writeErrorFile = true;

    /// <summary>Prefix for every log file name.</summary>
    public string logFilePrefix = "UnityDev-LOG";

    /// <summary>Logs folder path relative to the game's root.</summary>
    public string logFilesPath = "UnityDev_logs";

    /// <summary>Format of the timestamp in the file.</summary>
    // ReSharper disable once StringLiteralTypo
    public string logTsFormat = "yyMMddTHHmmss";

    public readonly CleanupPolicyGroup CleanupPolicy = new();
  }
  readonly ModuleConfig _settings = new();
  const string ConfigKeyName = "PersistentLog";

  protected override BaseLogAggregator.ModuleConfig Settings => _settings;
  internal override void LoadSettings() {
    var node = SimpleTextSerializer.LoadFromFile(PluginLoader.SettingsFileName, ignoreMissing: true)
        ?.GetNode(ConfigKeyName);
    if (node != null) {
      _settings.LoadFromConfigNode(node);
    }
  }
  #endregion

  /// <summary>Specifies if new record should be aggregated and persisted.</summary>
  bool _writeLogsToDisk;

  /// <summary>A writer that gets all the logs.</summary>
  StreamWriter _infoLogWriter;
  
  /// <summary>A writer for <c>WARNING</c>, <c>ERROR</c> and <c>EXCEPTION</c> logs.</summary>
  StreamWriter _warningLogWriter;

  /// <summary>Writer for <c>ERROR</c> and <c>EXCEPTION</c> logs.</summary>
  StreamWriter _errorLogWriter;

  /// <inheritdoc/>
  public override IEnumerable<LogRecord> GetLogRecords() {
    return LogRecords;  // It's always empty.
  }

  /// <inheritdoc/>
  public override void ClearAllLogs() {
    // Cannot clear persistent log so, restart the files instead.
    StartLogFiles();
  }

  /// <inheritdoc/>
  protected override void DropAggregatedLogRecord(LinkedListNode<LogRecord> node) {
    // Do nothing since there is no memory state in the aggregator.
  }

  /// <inheritdoc/>
  protected override void AggregateLogRecord(LogRecord logRecord) {
    if (!_writeLogsToDisk) {
      return;
    }
    var message = logRecord.MakeTitle();
    var type = logRecord.SrcLog.Type;
    if (type == LogType.Exception && logRecord.SrcLog.StackTrace.Length > 0) {
      message += "\n" + logRecord.SrcLog.StackTrace;
    }
    try {
      _infoLogWriter?.WriteLine(message);
      if (_warningLogWriter != null && type is LogType.Warning or LogType.Error or LogType.Exception) {
        _warningLogWriter.WriteLine(message);
      }
      if (_errorLogWriter != null && type is LogType.Error or LogType.Exception) {
        _errorLogWriter.WriteLine(message);
      }
    } catch (Exception ex) {
      _writeLogsToDisk = false;
      Debug.LogException(ex);
      Debug.LogError("Persistent log aggregator failed to write a record. Logging disabled");
    }
  }

  /// <inheritdoc/>
  public override void StartCapture() {
    base.StartCapture();
    StartLogFiles();
    PersistentLogAggregatorFlusher.ActiveAggregators.Add(this);
    if (_writeLogsToDisk) {
      Debug.Log("Persistent aggregator started");
    } else {
      Debug.LogWarning("Persistent aggregator disabled");
    }
  }

  /// <inheritdoc/>
  public override void StopCapture() {
    Debug.Log("Stopping a persistent aggregator...");
    base.StopCapture();
    StopLogFiles();
    PersistentLogAggregatorFlusher.ActiveAggregators.Remove(this);
  }

  /// <inheritdoc/>
  public override bool FlushBufferedLogs() {
    // Flushes accumulated logs to disk. In case of disk error the logging is disabled.
    var res = base.FlushBufferedLogs();
    if (res && _writeLogsToDisk) {
      try {
        _infoLogWriter?.Flush();
        _warningLogWriter?.Flush();
        _errorLogWriter?.Flush();
      } catch (Exception ex) {
        _writeLogsToDisk = false;  // Must be the first statement in the catch section!
        Debug.LogException(ex);
        Debug.LogError("Something went wrong when flushing data to disk. Disabling logging.");
      }
    }
    return res;
  }

  /// <inheritdoc/>
  protected override bool CheckIsFiltered(LogInterceptor.Log log) {
    return false;  // Persist any log!
  }

  /// <summary>Creates new logs files and redirects logs to there.</summary>
  void StartLogFiles() {
    StopLogFiles();  // In case something was opened.
    
    try {
      if (_settings.logFilesPath.Length > 0) {
        Directory.CreateDirectory(_settings.logFilesPath);
      }
      var tsSuffix = DateTime.Now.ToString(_settings.logTsFormat);
      if (_settings.writeInfoFile) {
        _infoLogWriter = new StreamWriter(
            Path.Combine(_settings.logFilesPath, $"{_settings.logFilePrefix}.{tsSuffix}.INFO.txt"));
      }
      if (_settings.writeWarningFile) {
        _warningLogWriter = new StreamWriter(
            Path.Combine(_settings.logFilesPath, $"{_settings.logFilePrefix}.{tsSuffix}.WARNING.txt"));
      }
      if (_settings.writeErrorFile) {
        _errorLogWriter = new StreamWriter(
            Path.Combine(_settings.logFilesPath, $"{_settings.logFilePrefix}.{tsSuffix}.ERROR.txt"));
      }
      _writeLogsToDisk = _infoLogWriter != null || _warningLogWriter != null || _errorLogWriter != null;
    } catch (Exception ex) {
      _writeLogsToDisk = false;  // Must be the first statement in the catch section!
      Debug.LogException(ex);
      Debug.LogError("Not enabling logging to disk due to errors");
    }
    try {
      CleanupLogFiles();
    } catch (Exception ex) {
      Debug.LogException(ex);
      Debug.LogError("Error happen while cleaning up old logs");
    }
  }

  /// <summary>Flushes and closes all opened log files.</summary>
  void StopLogFiles() {
    try {
      _infoLogWriter?.Close();
      _warningLogWriter?.Close();
      _errorLogWriter?.Close();
    } catch (Exception ex) {
      Debug.LogException(ex);
    }
    _infoLogWriter = null;
    _warningLogWriter = null;
    _errorLogWriter = null;
    _writeLogsToDisk = false;
  }

  /// <summary>Drops extra log files.</summary>
  void CleanupLogFiles() {
    if (_settings.CleanupPolicy.totalFiles < 0
        && _settings.CleanupPolicy.totalSizeMb < 0
        && _settings.CleanupPolicy.maxAgeHours < 0) {
      return;
    }
    if (!Directory.Exists(_settings.logFilesPath)) {
      return;
    }
    var logFiles = Directory.GetFiles(_settings.logFilesPath, _settings.logFilePrefix + ".*")
        .Select(x => new FileInfo(x))
        .OrderBy(x => x.CreationTimeUtc)
        .ToArray();
    if (logFiles.Length == 0) {
      Debug.Log("No log files found. Nothing to do");
      return;
    }
    var limitTotalSize = logFiles.Sum(x => x.Length);
    var limitExtraFiles = logFiles.Count() - _settings.CleanupPolicy.totalFiles;
    var limitAge = DateTime.UtcNow.AddHours(-_settings.CleanupPolicy.maxAgeHours);
    Debug.LogFormat("Found persistent logs: totalFiles={0}, totalSize={1}, oldestDate={2}",
                    logFiles.Count(), limitTotalSize, logFiles.Min(x => x.CreationTimeUtc));
    for (var i = 0; i < logFiles.Count(); i++) {
      var fieldInfo = logFiles[i];
      if (_settings.CleanupPolicy.totalFiles > 0 && limitExtraFiles > 0) {
        Debug.LogFormat("Drop log file due to too many log files exist: {0}", fieldInfo.FullName);
        File.Delete(fieldInfo.FullName);
      } else if (_settings.CleanupPolicy.totalSizeMb > 0
          && limitTotalSize > _settings.CleanupPolicy.totalSizeMb * 1024 * 1024) {
        Debug.LogFormat("Drop log file due to too large total size: {0}", fieldInfo.FullName);
        File.Delete(fieldInfo.FullName);
      } else if (_settings.CleanupPolicy.maxAgeHours > 0 && fieldInfo.CreationTimeUtc < limitAge) {
        Debug.LogFormat("Drop log file due to it's too old: {0}", fieldInfo.FullName);
        File.Delete(fieldInfo.FullName);
      }
      --limitExtraFiles;
      limitTotalSize -= fieldInfo.Length;
    }
  }
}

/// <summary>A helper class to periodically flush logs to disk.</summary>
sealed class PersistentLogAggregatorFlusher : MonoBehaviour {
  /// <summary>A list of persistent aggregators that need state flushing.</summary>
  internal static readonly List<PersistentLogAggregator> ActiveAggregators = new();

  /// <summary>A delay between flushes.</summary>
  const float PersistentLogsFlushPeriod = 0.2f; // Seconds.

  void Awake() {
    StartCoroutine(FlushLogsCoroutine());
  }

  void OnDestroy() {
    FlushAllAggregators();
  }

  /// <summary>Flushes all registered persistent aggregators.</summary>
  static void FlushAllAggregators() {
    var aggregators = ActiveAggregators.ToArray();
    foreach (var aggregator in aggregators) {
      aggregator.FlushBufferedLogs();
    }
  }

  /// <summary>Flushes logs to disk periodically.</summary>
  /// <remarks>This method never returns.</remarks>
  /// <returns>Delay till next flush.</returns>
  // ReSharper disable once MemberCanBeMadeStatic.Local
  IEnumerator FlushLogsCoroutine() {
    while (true) {
      yield return new WaitForSecondsRealtime(PersistentLogsFlushPeriod);
      FlushAllAggregators();
    }
    // ReSharper disable once IteratorNeverReturns
  }
}

}