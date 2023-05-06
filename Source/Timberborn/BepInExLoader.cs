// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using BepInEx;

// ReSharper disable once CheckNamespace
namespace UnityDev.LogConsole {

/// <summary>The first entry point into the game. We want to get installed as early as possible.</summary>
/// <remarks>
/// The BepInEx manager game objects die before the game start, so we'll need another try on TimberAPI bootstrap.
/// </remarks>
[BepInPlugin("55AD3765-51B3-4294-86F7-B6909C764BA3", "Unity LogConsole", "1.0.0")]
sealed class BepInExLoader : BaseUnityPlugin {
  void Awake() {
    Logger.LogInfo($"Attaching logger from BepInEx manager...");
    PluginLoader.Start();
  }
}

}
