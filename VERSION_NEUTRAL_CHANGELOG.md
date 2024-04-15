# Version neutral changes:
* [04/14/2024] Fix wrong fromatting and NPE when resolving filenames in some games (e.g. Timberborn).
* [04/13/2024] Support logging from the non-Unity threads. Such recodrs will have prefix "[Thread:#XXX]".
* [04/12/2024] A better check for the destroyed Unity onject.
* [04/12/2024] Intialize the console UI on first GUI usage instead of `Awake`. Not all games have
  the screen setup at the moment of injection.

# UnityDev_LogConsole v1.0 (May 4th, 2023):
* Initial version.
