@echo off
rem Builds a single VPinNexusVR.exe into .\publish (needs the .NET 10 Desktop Runtime on the PC that runs it).
dotnet publish "%~dp0VRPLauncher.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o "%~dp0publish"
