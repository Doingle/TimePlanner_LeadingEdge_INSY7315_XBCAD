# Time Planner Installer

Compile locally after publishing the widget:
```powershell
dotnet publish TimePlanner.Widget/TimePlanner.Widget.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/widget
iscc installer\TimePlanner.iss /DSourceDir=..\publish\widget
```

- Output goes to `dist\TimePlanner-Setup-<version>.exe`.
- The `AppId` is fixed forever (`{{B7E3C1A2-5D4F-4E8B-9A6C-2F1D0E3B7A95}}`) so upgrades find the previous install.
- Installs per user to `%LOCALAPPDATA%\Programs\TimePlanner` with no administrator prompt.
