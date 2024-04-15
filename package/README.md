# Overview

Hit `RightCtrl` in-game to open the console. Hit it again to hide. The logs, captured during the
play session, will be available under `<game root>/UnityDev_logs` folder. The files are grouped by
the log severity: INFO, WARNING, ERROR, EXCEPTION. The content of the log folder is automatically
maintained to not overflow the system. The default setting is to keep at most 30 log files, but it
can be changed via `CleanupPolicy` in the `settings.cfg` file.
