# Overview

Checkout `package/README.md`.

# UnityDev: LogConsole

`LogConsole` is an advanced console for in-game logging system. It supports persistense, filtering,
records grouping, and many more.

The main branch doesn't have a bootstrap code that would install this plugin into the game. In
different games different approaches may be needed. Check the branches for the specific game.

To install the plugin into the game, call `PluginLoader.Start` as soon as posible after the
application start.

## Timberborn branch

This branch contains code to install the console into Timberborn game. Do not merge this branch
into main! All non-game specific changes must be checked in to the `main`, and then merged back
to this branch.

NEVER PUSH CHANGES TO MAIN!!!
It's OK to do "pull from main", but it's never OK to "push to main".
