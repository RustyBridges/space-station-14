#!/usr/bin/env bash
# Starts a server running the Derelict co-op salvage mode.
# Settings live in Resources/ConfigPresets/derelict.toml.
dotnet run --project Content.Server -- --cvar config.presets=derelict "$@"
