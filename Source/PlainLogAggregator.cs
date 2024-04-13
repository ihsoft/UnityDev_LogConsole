// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using System.Collections.Generic;
using System.Linq;
using UnityDev.Utils.Configs;

// ReSharper disable once CheckNamespace
namespace UnityDev.LogConsole {

/// <summary>A log capture that just accumulates all logs in a plain list.</summary>
sealed class PlainLogAggregator : BaseLogAggregator {

  #region Settings
  const string ConfigKeyName = "PlainLogAggregator";

  protected override ModuleConfig Settings { get; } = new();
  internal override void LoadSettings() {
    var node = SimpleTextSerializer.LoadFromFile(PluginLoader.SettingsFileName, ignoreMissing: true)
        ?.GetNode(ConfigKeyName);
    if (node != null) {
      Settings.LoadFromConfigNode(node);
    }
  }
  #endregion

  /// <inheritdoc/>
  public override IEnumerable<LogRecord> GetLogRecords() {
    return LogRecords.ToArray().Reverse();
  }
  
  /// <inheritdoc/>
  public override void ClearAllLogs() {
    LogRecords.Clear();
    ResetLogCounters();
  }
  
  /// <inheritdoc/>
  protected override void DropAggregatedLogRecord(LinkedListNode<LogRecord> node) {
    LogRecords.Remove(node);
    UpdateLogCounter(node.Value, -1);
  }

  /// <inheritdoc/>
  protected override void AggregateLogRecord(LogRecord logRecord) {
    LogRecords.AddLast(new LogRecord(logRecord));
    UpdateLogCounter(logRecord, 1);
  }
}

}
