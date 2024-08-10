// Unity Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public Domain license.

using Timberborn.ModManagerScene;
using UnityDev.Utils.FSUtils;
using UnityDev.Utils.LogUtils;

// ReSharper disable once CheckNamespace
namespace UnityDev.LogConsole {

/// <summary>(Re-)starts the interception.</summary>
/// <remarks>
/// This method can be called multiple times. The first call will start the interception, the subsequent calls will do
/// nothing.
/// </remarks>
// ReSharper disable once UnusedType.Global
sealed class ModStarter : IModStarter {
  public void StartMod() {
    throw new System.NotImplementedException();
  }

  public void StartMod(IModEnvironment modEnvironment) {
    ModPaths.ModEnvironment = modEnvironment;
    DebugEx.Info("Starting logger...");
    PluginLoader.Start();
  }
}

}
