SteamXBox
Version 4.7 - win-x64


SteamXBox turns a game controller into a way of using Windows.

It began as a bridge exposing a Valve Steam Controller to games as a virtual Xbox 360
pad. It now also drives the desktop itself: the pointer, scrolling, text entry through
an on-screen keyboard, a full-screen environment, and a launcher that finds and opens
anything on the machine.

The goal is continuity. While Steam is running, Steam Input owns the controller. When
Steam closes, SteamXBox takes over so the controller keeps working - for navigation, for
typing, and for everything the desktop is normally used for.


Contents
--------
  SteamXBox.exe            The graphical application. This is the one to launch.
  SteamXBox.Core.exe       The controller runtime. Started and stopped by the GUI.
  SteamXBox.Desktop.exe    The full-screen environment and its control centre.
  Sc2XboxedSticks.Osk.exe  The on-screen keyboard driven by the sticks.
  Sc2XboxedPads.Osk.exe    The on-screen keyboard driven by the trackpads.
  SteamXBox.Indexer.exe    Builds a document index for an organisation. Optional,
                           run by an administrator, never by the end user.

USAGE.txt describes the launch modes, the included scripts, and the interface language.


Requirements
------------
  Windows 10 or 11, x64.
  ViGEmBus - required. https://github.com/nefarius/ViGEmBus/releases
  HidHide  - optional, only if a game reacts to both the physical and the virtual
             controller at once. https://github.com/nefarius/HidHide/releases

The full installer can install both drivers for you. The portable package and the
standard installer do not; install ViGEmBus yourself before first launch.

No .NET runtime is required: the executables are self-contained.


Controllers
-----------
Steam Controller, DualSense (PS5) and Xbox pads are supported, and any number of them at
once.

Each physical device is an independent input. Controllers do not cancel each other:
whichever one moves the pointer, moves it. Mouse and keyboard are never read and never
interfered with.

Every controller keeps its own settings - dead zones, curves, pointer speed, on-screen
keyboard placement - saved against a durable identity of that particular device, so a pad
is recognised as itself when it is plugged back in, even among several of the same model.
Profiles are edited in the Profils tab.

Note: DualSense and Xbox pads have sticks and no trackpads. Only the Steam Controller has
trackpads.


Two modes
---------
  Xbox360   The controller is passed through to games as a virtual Xbox 360 pad.
  Profile   The pads and sticks drive the pointer, scrolling and the keyboard overlay.

The Quick Access button switches between them.

WARNING: leaving Xbox360 mode is not instant. The controller firmware needs a few seconds
to restore its native layer, during which inputs may appear partly inactive.


On-screen keyboard
------------------
Two overlays, one driven by the sticks and one by the trackpads, each mapping the four
quadrants of the input to the four zones of the keyboard. Trackpad edges map to the edges
of the zone, so typing never requires reaching past the physical edge of the pad.

It can be pinned at the bottom centre of the screen holding the caret, or float near the
caret - to its left, above it, or below it when there is no room, since the right is where
the text is about to be written. Haptic feedback marks key hover and key press. The choice
is per controller.


The desktop environment
-----------------------
SteamXBox.Desktop is a full-screen environment holding a control centre: audio output,
Wi-Fi, Bluetooth, brightness, do-not-disturb, task manager, screen capture, calculator,
clipboard. Every tile is reachable with the arrow keys and therefore with a controller.

It is a backdrop, not a window. It covers the Windows desktop and stays below every other
window, so a tool launched from it is never buried by it, and it does not minimise.

  Shift Shift   Opens the search launcher.
  § §           Clears the screen; again, and the windows come back.


The search launcher
-------------------
Tapping Shift twice opens a launcher that searches applications, Start menu entries,
Microsoft Store apps, folders and connected drives, and opens what you pick. Enter opens
it; Ctrl+Enter shows it inside its folder. What you open is remembered, and ranks higher
next time.

Connected drives are shown above the search field and are searchable the moment they are
plugged in; removable ones can be ejected from there.

  D, steamxbox     Restricts the search to drive D.
  web: <words>     Searches the web, if a provider is configured.
  docs: <words>    Searches the organisation's own documents, if one is configured.


Web and document search
-----------------------
Both are optional, both are off until configured, and both are meant to be run by the
organisation rather than by us.

Web search goes to a SearXNG instance - free software, self-hosted, which the
administrator runs and controls. Nothing is sent to a search engine the organisation has
not chosen.

Document search goes to a Meilisearch instance holding the organisation's own corpus,
built by SteamXBox.Indexer.exe from a folder or a network share. It reads plain text, PDF,
Word, Excel and PowerPoint - both the current formats and the older .doc, .xls and .ppt -
along with OpenDocument, and the templates of both families. The older formats and
OpenDocument are read through LibreOffice when it is installed on the indexing machine;
LibreOffice is detected, never bundled.

An administrator can lock either provider so the setting cannot be changed on the
machine. This is the point of the feature for secondary schools, universities, non-profits
and organisations handling sensitive data.


Tools and plugins
-----------------
The environment is meant to be extended without recompiling anything, and without any
tool having to know how to draw a window: it declares what it contains, and the
environment draws it - themed, and navigable with a controller.

No plugin ever injects itself into another process. Plugins/README.md states the contract
in full. The loader is not written yet; the calculator and the clipboard are compiled in
and stand as the reference the loader will have to match.


Interface language
------------------
English and French. The interface follows the Windows display language on first launch;
you can override it in the Settings tab.


Licences
--------
SteamXBox is proprietary software, all rights reserved. See LICENSE.
Third-party components keep their own licenses; see THIRD-PARTY-NOTICES.txt.

Project page: https://github.com/Hoyatla/SteamXBox-Explorer
Community:    https://discord.gg/MmmvB5s3E
