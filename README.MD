# SystemOne Decisions

A .NET 10 console application with a colored ASCII banner and an interactive menu:

```text
1) Local System One - Choice
2) Local System One - Yes/No (noul)
3) Local System One -Score
0) Exit
```

Options 1–3 currently display a workflow placeholder. Press Enter to return to the menu, or select `0` to exit.

## Run

Install the .NET 10 SDK, then run from the repository root:

```sh
dotnet run --project SystemOneDecisionsConsole
```

Use a terminal that supports ANSI RGB colors to display the cyan banner and gold menu.

## Local API User Secrets

The project has a `UserSecretsId` configured. Store the local API URLs with .NET User Secrets, outside the repository. Each developer needs to set these values on their own machine.

| Secret key | Endpoint |
| --- | --- |
| `LocalSystemOneAPI` | `http://10.0.0.37:1234/v1/systemone` |
| `LocalDecisionsAPI` | `http://10.0.0.37:1234/v1/decisions` |

Run these commands from the repository root:

```sh
dotnet user-secrets set "LocalSystemOneAPI" "http://10.0.0.37:1234/v1/systemone" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
dotnet user-secrets set "LocalDecisionsAPI" "http://10.0.0.37:1234/v1/decisions" --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

Verify the configured values:

```sh
dotnet user-secrets list --project SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

User Secrets are a local development store and are not encrypted. The values above are endpoint URLs, not authentication tokens. The current menu placeholders do not load these settings or call either API yet.

## Build

```sh
dotnet build SystemOneDecisionsConsole/SystemOneDecisionsConsole.csproj
```

The root `.gitignore` excludes Visual Studio settings and .NET build artifacts.
