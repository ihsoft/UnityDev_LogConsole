# UnityDev: LogConsole

`LogConsole` is an advanced console for in-game logging system. It supports persistense, filtering,
records grouping, and many more.

The main branch doesn't have a bootstrap code that would install this plugin into the game. In
different games different approaches may be needed. Check the branches for the specific game.

To install the plugin into the game, call `PluginLoader.Start` as soon as posible after the
application start.

## Common branch

All common Unity functionality lives in `main` branch. For the game specific version, create or
chekout the specific branch. Do NOT merge specific branches into `main`! Only commits from `main`
to branch are allowed. The specific branches must only introduce the changes that are specific to
the game.
