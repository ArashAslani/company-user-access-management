# Dev Container

This folder configures a **Dev Container** (VS Code Dev Containers or GitHub Codespaces) with the .NET 10 SDK and C# Dev Kit.

## What this folder does
- Defines the development environment (SDK version, tools, extensions)
- Restores the solution after the container is created
- Forwards port 5000, where `dotnet run --project src/Web` listens

The database is a local SQLite file, so no database container is needed.

## When to use it
If using VS Code:
1. Install the "Dev Containers" extension.
2. Open the repository.
3. Select **"Reopen in Container"**.

If using GitHub Codespaces:
- Codespaces will automatically use this configuration when the workspace starts.

## Notes
This folder has no effect on the runtime application. It is only used to configure the developer environment.
