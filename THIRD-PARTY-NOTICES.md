# Third-party notices

This repository contains files that are not covered by this project's license (GNU AGPL-3.0, see `LICENSE`).

## Microsoft Flight Simulator SimConnect
- Files: `msfsclient/Lib/Microsoft.FlightSimulator.SimConnect.dll`, `msfsclient/Lib/SimConnect.dll`,
  `msfsclient/Lib/SimConnect.lib` and `msfsclient/Redist/SimConnect.msi`.
- Copyright Microsoft Corporation. These are redistributable files from the Microsoft Flight Simulator SDK, under the
  terms of the Microsoft Flight Simulator SDK license.
- The client links them to talk to the simulator. The additional permission in `README.md` covers that link.

## Barlow fonts
- Files: `msfsclient/Assets/Fonts/*.ttf` and `msfsclient.Core/Hud/fonts/*.ttf`.
- Copyright 2017 The Barlow Project Authors (https://github.com/jpt/barlow).
- License: SIL Open Font License, Version 1.1 (https://openfontlicense.org).

## Sample photos for the preview mode
- Files: `msfsclient/Assets/Preview/*.jpg`, cropped and scaled down. Only `--preview` shows them, as sample photo
  observations.
- From Wikimedia Commons, released into the public domain (CC0 1.0) by their authors:
  - `church.jpg`: "San Agustin Church Aerial View, Paoay, Ilocos Norte.jpg"
  - `lake.jpg`: "Aerial photograph of Lake Cachuma, CA.jpg"
  - `bridge.jpg`: "Aerial view of Bridge over Nooksack River 2019.jpg"

## NuGet packages
The client uses NuGet packages, for example the Windows App SDK, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite,
FirebaseAuthentication.net and Sentry. They are downloaded at build time, are not part of this repository, and come
under their own licenses.
