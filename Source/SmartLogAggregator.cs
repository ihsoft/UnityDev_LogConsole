// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using System.Collections.Generic;
using System.Linq;
using UnityDev.Utils.Configs;

// ReSharper disable once CheckNamespace
namespace UnityDev.LogConsole {

/// <summary>A log capture that aggregates logs globally by the content.</summary>
sealed class SmartLogAggregator : BaseLogAggregator {

  #region Settings
  const string ConfigKeyName = "SmartLogAggregator";

  protected override ModuleConfig Settings { get; } = new();
  internal override void LoadSettings() {
    var node = SimpleTextSerializer.LoadFromFile(PluginLoader.SettingsFileName, ignoreMissing: true)
        ?.GetNode(ConfigKeyName);
    if (node != null) {
      Settings.LoadFromConfigNode(node);
    }
  }
  #endregion

  /// <summary>Log index used by smart logging.</summary>
  readonly Dictionary<int, LinkedListNode<LogRecord>> _logRecordsIndex = new();

  /// <inheritdoc/>
  public override IEnumerable<LogRecord> GetLogRecords() {
    return LogRecords.ToArray().Reverse();
  }
  
  /// <inheritdoc/>
  public override void ClearAllLogs() {
    LogRecords.Clear();
    _logRecordsIndex.Clear();
    ResetLogCounters();
  }
  
  /// <inheritdoc/>
  protected override void DropAggregatedLogRecord(LinkedListNode<LogRecord> node) {
    LogRecords.Remove(node);
    _logRecordsIndex.Remove(node.Value.GetSimilarityHash());
    UpdateLogCounter(node.Value, -1);
  }

  /// <inheritdoc/>
  protected override void AggregateLogRecord(LogRecord logRecord) {
    if (_logRecordsIndex.TryGetValue(logRecord.GetSimilarityHash(), out var existingNode)) {
      LogRecords.Remove(existingNode);
      existingNode.Value.MergeRepeated(logRecord);
      LogRecords.AddLast(existingNode);
    } else {
      var node = LogRecords.AddLast(new LogRecord(logRecord));
      _logRecordsIndex.Add(logRecord.GetSimilarityHash(), node);
      UpdateLogCounter(logRecord, 1);
    }
  }
}

} // namespace KSPDev
