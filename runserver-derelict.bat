@echo off
rem Starts a server running the Derelict co-op salvage mode.
rem Settings live in Resources/ConfigPresets/derelict.toml.
dotnet run --project Content.Server -- --cvar config.presets=derelict %*
pause
