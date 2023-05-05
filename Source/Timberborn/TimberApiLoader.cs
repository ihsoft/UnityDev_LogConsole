// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using Bindito.Core;
using TimberApi.ConfiguratorSystem;
using TimberApi.SceneSystem;
using UnityDev.LogUtils;
using UnityEngine;

// ReSharper disable once CheckNamespace
namespace UnityDev.LogConsole {

/// <summary>(Re-)starts the interception in scope of TimberAPI.</summary>
/// <remarks>
/// The way how the modded game behaves today, the BepInEx game objects get destroyed before the game start. Thus,
/// here we re-installing the mod with a brand new object. The ModLoader will handle it just fine.
/// </remarks>
[Configurator(SceneEntrypoint.MainMenu)]
// ReSharper disable once UnusedType.Global
sealed class TimberApiLoader : IConfigurator {
  public void Configure(IContainerDefinition containerDefinition) {
    DebugEx.Info("Re-trying LogConsole loading from TimberAPI...");
    PluginLoader.Start(new GameObject("unity_extended_log_console"));
  }
}

}
