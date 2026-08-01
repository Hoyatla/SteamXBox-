SteamXBox
Version 3.2 - win-x64

SteamXBox is a Windows bridge that exposes a Valve Steam Controller as a virtual
Xbox 360 controller, and lets the same controller drive the Windows desktop when no
game is running: mouse pointer, scrolling, and text entry through an on-screen
keyboard overlay.

The goal is continuity. While Steam is running, Steam Input owns the controller.
When Steam closes, SteamXBox takes over so the controller keeps working for
navigation and typing.


Contents
--------
  SteamXBox.exe         The graphical application. This is the one to launch.
  SteamXBox.Core.exe    The controller runtime. Started and stopped by the GUI.
  Sc2Xboxed.Osk.exe     The on-screen keyboard overlay.

USAGE.txt describes the launch modes, the included scripts, and the interface
language.


Requirements
------------
  Windows 10 or 11, x64.
  ViGEmBus - required. https://github.com/nefarius/ViGEmBus/releases
  HidHide  - optional, only if a game reacts to both the physical and the virtual
             controller at once. https://github.com/nefarius/HidHide/releases

The full installer can install both drivers for you. The portable package and the
standard installer do not; install ViGEmBus yourself before first launch.

No .NET runtime is required: the executables are self-contained.


Two modes
---------
  Xbox360   The controller is passed through to games as a virtual Xbox 360 pad.
  Profile   The pads drive the mouse pointer, scrolling and the keyboard overlay.

The Quick Access button switches between them.

WARNING: leaving Xbox360 mode is not instant. The controller firmware needs a few
seconds to restore its native layer, during which inputs may appear partly inactive.


Interface language
------------------
English and French. The interface follows the Windows display language on first
launch; you can override it in the Settings tab.


Licences
--------
See COPYING-GPL-3.0.txt and LICENSE-LGPL-3.0.txt.

Project page: https://github.com/Hoyatla/SteamXBox
Community:    https://discord.gg/MmmvB5s3E
