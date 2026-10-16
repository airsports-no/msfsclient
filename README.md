# Air Sports Live Tracking client for Microsoft Flight Simulator

The Windows client that lets you fly [Air Sports Live Tracking](https://airsports.no) competitions in Microsoft Flight
Simulator 2020 and 2024: air navigation races, precision flying and other navigation tasks, with a live map and
automatic scoring. It sends the simulator aircraft's position to Air Sports Live Tracking while you fly, and has the
features and look of the Air Sports mobile apps. It also includes a toolbar panel for MSFS and a flight display for a
second screen.

Pilots download it from [flightsim.to](https://flightsim.to) or the Microsoft Store. The server and website are in
[airsports-no/airsportslivetracking](https://github.com/airsports-no/airsportslivetracking).

## Repository
- `msfsclient.Core/`: the app logic, without UI (.NET 8): API client, app state, tracking, observations, the in-sim
  flight display server.
- `msfsclient.Core.Tests/`: the tests (xUnit).
- `msfsclient/`: the WinUI 3 app (Windows App SDK).
- `msfs-panel/`: the MSFS toolbar panel.
- `release/`: the flightsim.to bundle, its README and the listing text.
- `GEMINI.md`: architecture notes for developers and AI assistants.

## Build and test
- Requirements: Windows 10 1809 or later, the .NET 8 SDK, and Visual Studio 2022 or later with the Windows App SDK
  workload for the app.
- Tests: `dotnet test msfsclient.Core.Tests`.
- App: open `msfsclient.slnx` in Visual Studio, or run `dotnet build msfsclient/msfsclient.csproj -p:Platform=x64`.
- `msfsclient.exe --preview [screen]` shows the screens with sample data, without an account or MSFS.
- `--base-url http://localhost:8002 --traccar-url http://localhost:5055` runs the client against a local server.

## License
Copyright (C) 2026 the Air Sports Live Tracking contributors.

This program is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General
Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any
later version. This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even
the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the [LICENSE](LICENSE) file for
details.

**Additional permission under GNU AGPL version 3 section 7:** if you modify this program, or any covered work, by
linking or combining it with the Microsoft Flight Simulator SimConnect libraries (or a modified version of those
libraries), containing parts covered by the terms of the Microsoft Flight Simulator SDK license, the licensors of this
program grant you additional permission to convey the resulting work.

Third-party components keep their own licenses. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
