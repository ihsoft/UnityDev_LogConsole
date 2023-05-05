﻿// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using StackTrace = System.Diagnostics.StackTrace;
using StackFrame = System.Diagnostics.StackFrame;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics.CodeAnalysis;
using UnityDev.Utils.Configs;
using UnityEngine;

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
  }

  /// <summary>Removes log interceptor and allows logs flowing into the system.</summary>
  public static void StopIntercepting() {
    if (!IsStarted) {
      return;  // NOOP if already stopped.
    }
    Debug.LogWarning("Debug output returns back to the system. Use system's console to see the logs");
    Application.logMessageReceived -= HandleLog;
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

  /// <summary>Records a log from the log callback.</summary>
  /// <param name="message">The message to log.</param>
  /// <param name="exceptionStackTrace">The exception stack trace provided by the Unity core.</param>
  /// <param name="type">The type of message (error, exception, warning, assert).</param>
  static void HandleLog(string message, string exceptionStackTrace, LogType type) {
    // Detect source and stack trace for logs other than exceptions. Exceptions are logged from
    // the Unity engine, and the provided stack trace should be used. 
    var stackTrace = exceptionStackTrace;
    StackFrame[] frames = null;
    var source = type != LogType.Exception
        ? GetSourceAndStackTrace(out stackTrace, out frames)
        : GetSourceAndReshapeStackTrace(ref stackTrace);
    var log = new Log(_lastLogId++) {
        Timestamp = DateTime.Now,
        Message = message,
        StackTrace = stackTrace,
        StackFrames = frames,
        Source = source,
        Type = type,
    };

    // Notify preview handlers. Do an exception check and exclude preview callbacks that cause errors.
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
  /// <param name="stackTrace">
  /// The string representation of the applicable stack strace. The format is undetermined, so parsing it would be a bad
  /// idea.
  /// </param>
  /// <param name="frames">
  /// The related stack frames fro the stack trace. It can be <c>null</c> if the method has failed to capture the proper
  /// stack trace.
  /// </param>
  /// <returns>A string that identifies a meaningful piece of code that triggered the log.</returns>
  static string GetSourceAndStackTrace(out string stackTrace, out StackFrame[] frames) {
    StackTrace st;
    string source;

    var skipFrames = 2;  // +1 for calling from HandleLogs(), +1 for Unity last method.
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
