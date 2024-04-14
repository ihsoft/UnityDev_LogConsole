// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using StackTrace = System.Diagnostics.StackTrace;
using StackFrame = System.Diagnostics.StackFrame;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using UnityDev.Utils.Configs;
using UnityEngine;
using Object = UnityEngine.Object;

// ReSharper disable once CheckNamespace
namespace UnityDev.LogConsole {
  
/// <summary>An alternative log processor that allows better logs handling.</summary>
/// <remarks>Keep it static!</remarks>
public static class LogInterceptor {
  #region Settings
  [SuppressMessage("ReSharper", "InconsistentNaming")]
  [SuppressMessage("ReSharper", "CollectionNeverUpdated.Local")]
  class ModuleConfig : PersistentNode {
    /// <summary>Shifts stack trace forward by the exact source match.</summary>
    /// <remarks>
    /// Use this filter to skip well-known methods that wrap logging. Due to hash-match this set can be reasonable big
    /// without significant impact to the application performance.
    /// </remarks>
    public readonly HashSet<string> exactMatchOverride = new();

    /// <summary>Skips all the matched prefixes up in the stack trace until a non-matching source is found.</summary>
    /// <remarks>
    /// Use this filter when logging is wrapped by a distinct module that may emit logging from different methods. This
    /// filter is handled via "full scan" approach so, having it too big may result in a degraded application
    /// performance.
    /// </remarks>
    public readonly List<string> prefixMatchOverride = new();
    
    /// <summary>Specifies if logs interception is allowed.</summary>
    /// <remarks>If <c>false</c> then calls to <see cref="StartIntercepting"/> will be ignored.</remarks>
    /// <seealso cref="StartIntercepting"/>
    /// <seealso cref="StopIntercepting"/>
    public bool enableInterception = true;
  }
  static readonly ModuleConfig Settings = new();
  const string ConfigKeyName = "LogInterceptor";

  internal static void LoadSettings() {
    var node = SimpleTextSerializer.LoadFromFile(PluginLoader.SettingsFileName, ignoreMissing: true)
        ?.GetNode(ConfigKeyName);
    if (node != null) {
      Settings.LoadFromConfigNode(node);
    }
  }
  #endregion

  #region Helper Unity object to periodically flush losg from the other therads

  sealed class ThreadLogsUpdater : MonoBehaviour {
    void Awake() {
      Application.logMessageReceivedThreaded += HandleThreadLog;
    }

    void OnDestroy() {
      Application.logMessageReceivedThreaded -= HandleThreadLog;
    }

    void Update() {
      FlushThreadLogs();
    }
  }

  #endregion

  /// <summary>A basic container for the incoming logs. Immutable.</summary>
  public class Log {
    public readonly int Id;
    public DateTime Timestamp;
    public string Message;
    public string StackTrace;
    public StackFrame[] StackFrames;
    public string Source;
    public LogType Type;
    public bool FilenamesResolved;
    
    internal Log(int id) {
      this.Id = id;
    }

    internal Log(Log srcLog) {
      Id = srcLog.Id;
      Timestamp = srcLog.Timestamp;
      Message = srcLog.Message;
      StackTrace = srcLog.StackTrace;
      StackFrames = srcLog.StackFrames;
      Source = srcLog.Source;
      Type = srcLog.Type;
    }
  }

  /// <summary>Intercepting mode. When disabled all logs go to the system.</summary>
  public static bool IsStarted { get; private set; }

  /// <summary>Callback type for the log listeners.</summary>
  /// <param name="log">An immutable log record.</param>
  public delegate void PreviewCallback(Log log);
  static readonly HashSet<PreviewCallback> PreviewCallbacks = new();

  /// <summary>Collection to accumulate callbacks that throw errors.</summary>
  /// <remarks>
  /// A preview callback that throws an exception is unregistered immediately to minimize the impact. This collection is
  /// used locally only in <see cref="HandleLog"/> but to save performance it's created statically with a reasonable
  /// pre-allocated size.
  /// </remarks>
  static readonly List<PreviewCallback> BadCallbacks = new(10);

  /// <summary>Unique identifier of the log record.</summary>
  static int _lastLogId = 1;

  /// <summary>Installs interceptor callback and disables system debug log.</summary>
  public static void StartIntercepting() {
    if (!Settings.enableInterception || IsStarted) {
      return;  // NOOP if already started or disabled.
    }
    Debug.LogWarning("Debug output intercepted by Unity LogConsole.");
    IsStarted = true;

    Application.logMessageReceived += HandleLog;
    PluginLoader.MainGameObject.AddComponent<ThreadLogsUpdater>();
  }

  /// <summary>Removes log interceptor and allows logs flowing into the system.</summary>
  public static void StopIntercepting() {
    if (!IsStarted) {
      return;  // NOOP if already stopped.
    }
    Debug.LogWarning("Debug output returns back to the system. Use system's console to see the logs");
    Application.logMessageReceived -= HandleLog;
    Object.DestroyImmediate(PluginLoader.MainGameObject.GetComponent<ThreadLogsUpdater>());
    IsStarted = false;
  }
  
  /// <summary>Registers a callback that is called on every log record intercepted.</summary>
  /// <remarks>If there are multiple callbacks registered then they are called in an undetermined order.</remarks>
  /// <param name="previewCallback">Callback to register.</param>
  public static void RegisterPreviewCallback(PreviewCallback previewCallback) {
    PreviewCallbacks.Add(previewCallback);
  }

  /// <summary>Unregisters log preview callback.</summary>
  /// <param name="previewCallback">A callback to unregister.</param>
  public static void UnregisterPreviewCallback(PreviewCallback previewCallback) {
    PreviewCallbacks.Remove(previewCallback);
  }

  /// <summary>Creates a log records out of the log callback arguments.</summary>
  /// <param name="message">The message to log.</param>
  /// <param name="exceptionStackTrace">The exception stack trace provided by the Unity core.</param>
  /// <param name="type">The type of message (error, exception, warning, assert).</param>
  /// <param name="skipFrames">Number of frames to skip to get the entry point (Unity callback).</param>
  static Log MakeLogRecord(string message, string exceptionStackTrace, LogType type, int skipFrames) {
    // Detect source and stack trace for logs other than exceptions. Exceptions are logged from
    // the Unity engine, and the provided stack trace should be used. 
    var stackTrace = exceptionStackTrace;
    StackFrame[] frames = null;
    var source = type != LogType.Exception
        ? GetSourceAndStackTrace(skipFrames + 1, out stackTrace, out frames)
        : GetSourceAndReshapeStackTrace(ref stackTrace);
    return new Log(Interlocked.Increment(ref _lastLogId)) {
        Timestamp = DateTime.Now,
        Message = message,
        StackTrace = stackTrace,
        StackFrames = frames,
        Source = source,
        Type = type,
    };
  }

  /// <summary>Delivers log to the preview handlers.</summary>
  /// <remarks>If a handler throws an error it's get removed and won't get other updates.</remarks>
  /// <param name="log"></param>
  static void SendToPreviewHandlers(Log log) {
    foreach (var callback in PreviewCallbacks) {
      try {
        callback(log);
      } catch (Exception) {
        BadCallbacks.Add(callback);
      }
    }
    if (BadCallbacks.Count > 0) {
      PreviewCallbacks.RemoveWhere(BadCallbacks.Contains);
      Debug.LogErrorFormat("Dropped {0} bad log preview callbacks", BadCallbacks.Count);
      BadCallbacks.Clear();
    }
  }

  /// <summary>Records a log from the log callback.</summary>
  /// <param name="message">The message to log.</param>
  /// <param name="exceptionStackTrace">The exception stack trace provided by the Unity core.</param>
  /// <param name="type">The type of message (error, exception, warning, assert).</param>
  static void HandleLog(string message, string exceptionStackTrace, LogType type) {
    SendToPreviewHandlers(MakeLogRecord(message, exceptionStackTrace, type, 1));
  }

  /// <summary>Records a log from non-main Unity thread.</summary>
  /// <remarks>
  /// This method is thread safe. It doesn't immediately send logs to the pipeline. Instead, it only collects them.
  /// <see cref="FlushThreadLogs"/> is responsible for delivering logs to the preview handlers.
  /// </remarks>
  /// <param name="message">The message to log.</param>
  /// <param name="exceptionStackTrace">The exception stack trace provided by the Unity core.</param>
  /// <param name="type">The type of message (error, exception, warning, assert).</param>
  static void HandleThreadLog(string message, string exceptionStackTrace, LogType type) {
    var threadedMessage = "[Thread:#" + Thread.CurrentThread.ManagedThreadId + "] " + message;
    _threadLogRecords.Enqueue(MakeLogRecord(threadedMessage, exceptionStackTrace, type, 1));
  }

  /// <summary>Sends the accumulated logs from teh threads to the main pipeline.</summary>
  /// <remarks>
  /// This method must only be called from the main Unity thread. It's OK better to call it frequently to not let the
  /// logs to pile up.
  /// </remarks>
  static void FlushThreadLogs() {
    if (_threadLogRecords.IsEmpty) {
      return;
    }
    var threadLogs = Interlocked.Exchange(ref _threadLogRecords, new ConcurrentQueue<Log>());
    foreach (var log in threadLogs) {
      SendToPreviewHandlers(log);
    }
  }

  static ConcurrentQueue<Log> _threadLogRecords = new();

  /// <summary>Calculates log source and the related stack trace.</summary>
  /// <remarks>
  /// The stack trace grabbed from the current calling point can be really big because it usually comes from a generic
  /// Unity methods, game libraries or an addon debug wrapper modules. While it's just inconvenient when investigating
  /// the logs it's a huge problem when calculating the "source", a meaningful piece of code that actually did the
  /// logging. In normal case it's a full method name but when logging is wrapped in several helper methods deducting it
  /// may become a problem. This method does checks for exact (<see cref="ModuleConfig.exactMatchOverride"/>) and prefix
  /// (<see cref="ModuleConfig.prefixMatchOverride"/>) matches of the source to exclude sources that don't make sense.
  /// Fine tuning of the matches is required to have perfectly clear logs.
  /// </remarks>
  /// <p>
  /// This method assumes it's two levels down in the calling stack from the last Unity's method
  /// (which is <c>UnityEngine.Application.CallLogCallback</c> for now).
  /// </p>
  /// <param name="skipFrames">
  /// Number of frames to skip in teh stack to reach the log system entry point (the Unity callback).
  /// </param>
  /// <param name="stackTrace">
  /// The string representation of the applicable stack strace. The format is undetermined, so parsing it would be a bad
  /// idea.
  /// </param>
  /// <param name="frames">
  /// The related stack frames fro the stack trace. It can be <c>null</c> if the method has failed to capture the proper
  /// stack trace.
  /// </param>
  /// <returns>A string that identifies a meaningful piece of code that triggered the log.</returns>
  static string GetSourceAndStackTrace(int skipFrames, out string stackTrace, out StackFrame[] frames) {
    StackTrace st;
    string source;

    skipFrames++;  // +1 to get the last Unity method.
    while (true) {
      st = new StackTrace(skipFrames, true);
      if (st.FrameCount == 0) {
        // Internal errors (like UnityEngine or core C#) can be logged directly through the callback. In which case the
        // stack trace doesn't have any useful information. Return an empty stack and "UNKNOWN" source when it happens.
        stackTrace = "<System call>";
        frames = null;
        return "UNKNOWN";
      }
      source = MakeSourceFromFrame(st.GetFrame(0));

      // Check if exactly this source is blacklisted.
      if (Settings.exactMatchOverride.Contains(source)) {
        ++skipFrames;
        continue;  // Re-run overrides for the new source.
      }

      // Check if the whole namespace prefix needs to be skipped in the trace.
      var prefixFound = false;
      foreach (var prefix in Settings.prefixMatchOverride) {
        if (source.StartsWith(prefix)) {
          prefixFound = true;
          ++skipFrames;
          for (var frameNum = 1; frameNum < st.FrameCount; ++frameNum) {
            if (!MakeSourceFromFrame(st.GetFrame(frameNum)).StartsWith(prefix)) {
              break;
            }
            ++skipFrames;
          }
          break;
        }
      }
      if (prefixFound) {
        continue;  // There is a prefix match, re-try all the filters.
      }
      
      // No overrides.
      break;
    }
    
    stackTrace = st.ToString();  // Unity only gives stacktrace for the exceptions.
    frames = st.GetFrames();
    return source;
  }

  /// <summary>Calculates source from the Unity exception stack trace.</summary>
  /// <remarks>Also, modifies the stack trace to make it looking more C#ish.</remarks>
  /// <param name="stackTrace">Unity provided stack trace.</param>
  /// <returns>Source extracted from the first line of the trace.</returns>
  static string GetSourceAndReshapeStackTrace(ref string stackTrace) {
    var lines = stackTrace.Split(new[] {'\n'}, StringSplitOptions.RemoveEmptyEntries);
    stackTrace = string.Join("\n", (from line in lines select "   at " + line).ToArray());
    if (lines.Length > 0 && !string.IsNullOrEmpty(lines[0])) {
      var line = lines[0];
      return line.Substring(0, line.IndexOfAny(new[] {' ', '('})).Trim();
    }
    return "";
  }

  /// <summary>Makes source string from the frame.</summary>
  /// <param name="frame">A stack frame to make string from.</param>
  /// <returns>A source string.</returns>
  static string MakeSourceFromFrame(StackFrame frame) {
    var chkMethod = frame.GetMethod();
    return chkMethod.DeclaringType + "." + chkMethod.Name;
  }
}

} // namespace UnityDev
