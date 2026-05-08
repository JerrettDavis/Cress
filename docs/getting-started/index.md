# Getting started

This section helps you get from a fresh clone to a first successful Cress run. Pick the path that matches your system under test.

![Studio landing page](../images/studio/landing.png)

## Prerequisites

- Windows for the full Studio and Flawright-backed desktop experience
- .NET SDK `10.0.107` or later
- Node.js `22.x` for the Node workspaces and Playwright-backed browser tooling

## Choose the right onboarding path

| Path | Use it when | Outcome |
| --- | --- | --- |
| [Fastest first run](quickstart-first-run.md) | you want the quickest proof that Cress can validate and execute a real flow | pack the CLI as a local tool and run the built-in HTTP sample without a full solution build |
| [HTTP quickstart](quickstart-http.md) | you want the fastest end-to-end success with no browser or desktop dependency | validate, run, and publish a living doc from the sample project |
| [Web quickstart](quickstart-web.md) | you need browser automation with Studio or Studio Web | create a project, configure a profile, record or author a browser flow |
| [Desktop quickstart](quickstart-desktop.md) | you need Windows desktop automation with Flawright | enable the desktop driver, record a desktop flow, and review evidence |

## Recommended first-session flow

1. Run the [Fastest first run](quickstart-first-run.md) path if you want the quickest evaluation loop.
2. Run the HTTP quickstart so you can see the project layout and report outputs.
3. Open Studio or Studio Web to learn the authoring surfaces.
4. Move to the web or desktop quickstart for your real system.

![Project loaded in Studio](../images/studio/project-loaded.png)

## Core commands you will use first

```powershell
dotnet tool restore
dotnet restore Cress.sln
dotnet build Cress.sln --configuration Release
dotnet run --project src\Cress.Cli\Cress.Cli.csproj -- --help
```

## What “first success” looks like

After the onboarding flow you should be able to:

- identify a Cress project root
- understand the `.cress`, `capabilities`, `flows`, `fixtures`, and `steps` folders
- validate a project before running it
- inspect generated artifacts, reports, and living docs
