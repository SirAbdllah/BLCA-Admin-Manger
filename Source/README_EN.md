# BLCA Admin Manger 1.1.4

Developed by BLCA Development Team.

The `Source` folder contains the editable manager source, its core library, tests, and configuration examples. `BLCA Admin Manger.exe` alongside `Source` is the published Windows x64 release and includes its runtime. To run source changes, build the application and launch the executable from its output folder.

## Build from source

Use Windows and the .NET 8 SDK. `global.json` selects SDK 8.0.423 or a later installed .NET 8 feature band. The Windows desktop targeting packs are required for the Windows Forms application.

Run these PowerShell commands from this folder:

```powershell
dotnet restore .\Manager\App\BLCA.Admin.Manager.csproj --configfile .\NuGet.Config
dotnet build .\Manager\App\BLCA.Admin.Manager.csproj -c Release --no-restore
dotnet restore .\Tests\BLCA.Admin.Tests.csproj --configfile .\NuGet.Config
dotnet run --project .\Tests\BLCA.Admin.Tests.csproj -c Release --no-restore
```

The application is written to `Manager\App\bin\Release\net8.0-windows\BLCA Admin Manger.exe`. Run it with the other files in that output folder. This build requires the .NET 8 Desktop Runtime on the computer that runs it.

```powershell
& '.\Manager\App\bin\Release\net8.0-windows\BLCA Admin Manger.exe'
```

`NuGet.Config` clears remote package sources. The projects use local project references and contain no external package references. An offline build needs the SDK and targeting packs already installed. A publish that includes the runtime also needs its runtime packs available locally or a configured package source.

To create a Windows x64 package that includes its runtime when the required runtime packs are available:

```powershell
dotnet publish .\Manager\App\BLCA.Admin.Manager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --configfile .\NuGet.Config -o .\publish
```

This command creates `Source\publish\BLCA Admin Manger.exe` with its required runtime. Run the new executable from `publish` or copy the publish output you intend to distribute into the release folder. Updating the ready-to-run executable beside `Source` requires copying the published output there.

```powershell
& '.\publish\BLCA Admin Manger.exe'
```

The application project embeds `Manager\App\Assets\BLCA_Logo.png` and uses `Manager\App\Assets\BLCA.ico` as its Windows executable icon. Preserve both assets when copying the source.

## Moving existing server data

When upgrading an existing server, first stop the server, manager, and any background agent. Keep a copy of the current administration data folder in the actual server profile, then rename that folder to `BLCAAdminSuite` in the same location. Preserve its complete contents, including `config.json`, `config.rev`, `audit`, `manager-data`, bans, and other files. Run the updated addon together with BLCA Admin Manger 1.1.4, then select the profile or `BLCAAdminSuite` folder in the application. Existing permissions, bans, and history continue loading after this manual folder move.

To retain local folder selections, close the application and rename its current preferences folder under `%LOCALAPPDATA%` to `BLCAAdminManager`, preserving `settings.json`. You can also select the server profile and log folder again in the interface after updating.

## Connect to a server profile

Start `BLCA Admin Manger.exe`, select the actual Arma Reforger server profile folder, and connect. You can also select the `BLCAAdminSuite` folder inside that profile. The manager reads and writes the files that the compatible server addon uses in that folder. If the server runs on another computer, the selected folder must expose those actual server files with the required access.

The server addon creates its profile data while the server is running. Configure staff in the manager, then use the in-game Admin Panel to log in and activate the assigned staff session. `Examples\config.json` and `Examples\trusted-player-metadata.csv` show the supported data formats. Review example values before using them with a live profile.

To keep processing audit files and queued requests while the main window is closed, start the background agent with the actual profile path. Add the native log folder when native editor log import is needed:

```powershell
& '.\BLCA Admin Manger.exe' --agent --profile 'C:\ReforgerServer\profile' --logs 'C:\ReforgerServer\profile\logs'
```

Run that command from the build or publish output folder. The agent appears in the Windows notification area. Use its menu to stop it.

Local preferences are stored in `%LOCALAPPDATA%\BLCAAdminManager\settings.json`, and server data is stored in `BLCAAdminSuite` inside the server profile. Internal projects and namespaces use `BLCA.Admin`.

The full usage guides are `..\README_AR.md` and `..\README_EN.md`. The embedded artwork retains its original BLC lettering. The product name is BLCA Admin Manger.
