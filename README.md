# BrowserSearch
This is a plugin for PowerToys Run.
It reads your default browser's history and bookmarks, allowing you to search their entries and open their URL.

## Search sources
The plugin's settings in PowerToys have two independent checkboxes:
* **Search history** (enabled by default)
* **Search bookmarks** (disabled by default, only supported on Chromium-based browsers)

When both are enabled, a URL that is both bookmarked and in the history is shown once, as a bookmark.

<p align="center">
    <img src="./Screenshots/1.png" width="500"/>
</p>

## Supported browsers
* Arc
* Brave
* Cent Browser
* Chromium
* Firefox
* Google Chrome
* LibreWolf
* Microsoft Edge (Chromium version)
* Naver Whale
* Opera
* Opera GX (profile selection not supported)
* Thorium
* Ungoogled Chromium
* Vivaldi Browser
* Waterfox
* Wavebox
* Zen Browser

Support for any other browser based on Chromium or Firefox can be added easily. If yours is not listed here, open an issue.

**NOTE**: Some browsers share the same ID. For example, if you have both Opera and Opera Developer installed, only the history of Opera will be loaded no matter which one of them is set as the default browser since we can't differentiate them by their ID.

## Install instructions
* Exit PowerToys
* Download latest version from [releases](https://github.com/TBM13/BrowserSearch/releases)
* Extract zip
* Move extracted folder `BrowserSearch` to `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\`
* Start PowerToys

## Build instructions
Requirements:
* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
* [just](https://github.com/casey/just)
* [PowerShell 7+](https://github.com/PowerShell/PowerShell) (`pwsh`)
* PowerToys 0.102.0 or newer, installed per-user or per-machine

Clone this repo and run the recipes from its root folder:

| Command | Description |
|---|---|
| `just` | List the available recipes |
| `just clean` | Remove `bin`, `obj`, `libs` and `PublishOutput` |
| `just check` | Check that PowerToys is installed, is new enough and targets the same .NET version as the project |
| `just libs` | Run `check`, then copy the required PowerToys DLLs to `BrowserSearch\libs` |
| `just build` | Run `libs`, then make a release build |
| `just publish` | Run `build`, then output all the necessary files to the `PublishOutput` folder |
| `just install` | Run `publish`, then copy the plugin to `%LOCALAPPDATA%\Microsoft\PowerToys\PowerToys Run\Plugins\BrowserSearch\` |

If PowerToys is running, `just install` stops all of its processes (closing the Settings window if it's open) and starts it again afterwards. If PowerToys is running as administrator, run the recipe from an elevated shell.

To make a debug build, use `just configuration=Debug build`.