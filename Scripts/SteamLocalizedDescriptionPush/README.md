# Steam Workshop Localized Description Updater

A standalone C# tool that updates Steam Workshop localized descriptions for DuravoQOLMod using the Steamworks API.

## How Authentication Works

The Steamworks API uses **Steam client session authentication** - there's no API key, OAuth token, or password involved.

When the tool runs:
1. It creates a `steam_appid.txt` file with tModLoader's App ID (1281930)
2. Calls `SteamAPI.Init()` which connects to your **running Steam client** via IPC
3. The Steam client already has you logged in, so Steamworks inherits that session
4. For Workshop operations, Steam checks if the logged-in account has edit permissions for the item

This is why Steam must be running and you must be logged in as the mod owner - the tool literally uses your active Steam session.

## Prerequisites

1. **Steam must be running** and you must be logged in as the mod owner
2. **.NET 8.0 SDK** installed
3. **Steamworks SDK v1.57** - The Steamworks.NET 20.1.0 NuGet package requires this specific version

## Setting Up Steam Native Libraries

**IMPORTANT:** Steamworks.NET 20.1.0 requires Steamworks SDK v1.57. Newer SDK versions have breaking changes.

Download SDK v1.57 from: https://partner.steamgames.com/downloads/list (you may need to check "Previous Versions")

Then copy the native DLL:
```
FROM: sdk/redistributable_bin/win64/steam_api64.dll
TO:   Scripts/SteamLocalizedDescriptionPush/bin/Debug/net8.0/steam_api64.dll
```

Alternatively, copy from your tModLoader installation:
```
FROM: C:\Program Files (x86)\Steam\steamapps\common\tModLoader\steam_api64.dll
TO:   Scripts/SteamLocalizedDescriptionPush/bin/Debug/net8.0/steam_api64.dll
```

## Installation

```powershell
# From the project root
cd Scripts/SteamLocalizedDescriptionPush
dotnet restore
dotnet build
```

## Usage

```powershell
# From anywhere within the DuravoQOLMod project
dotnet run --project Scripts/SteamLocalizedDescriptionPush
```

The tool will:
1. Find the project root by looking for `description_workshop.txt`
2. Load the primary English description from `description_workshop.txt`
3. Load all localized descriptions from `Localization/description_workshop/*.bbcode`
4. Update the Steam Workshop item with all localized descriptions

## Supported Languages

| Locale Code | Steam Language | File |
|-------------|----------------|------|
| (primary) | english | `description_workshop.txt` |
| de-DE | german | `description_workshop-de-DE.bbcode` |
| es-ES | spanish | `description_workshop-es-ES.bbcode` |
| fr-FR | french | `description_workshop-fr-FR.bbcode` |
| it-IT | italian | `description_workshop-it-IT.bbcode` |
| pt-BR | brazilian | `description_workshop-pt-BR.bbcode` |
| ru-RU | russian | `description_workshop-ru-RU.bbcode` |
| pl-PL | polish | `description_workshop-pl-PL.bbcode` |
| zh-Hans | schinese | `description_workshop-zh-Hans.bbcode` |

## Troubleshooting

### "Failed to initialize Steam API"
- Make sure Steam is running
- Make sure you're logged in as the mod owner (jeske)
- Make sure Steam client has tModLoader installed

### "Steam returned: k_EResultFail"
- Check that the Workshop item ID is correct
- Verify you have permissions to edit the item
- Check the Steam Workshop legal agreement has been accepted

## How It Works

The tool uses the Steamworks.NET NuGet package to access the `ISteamUGC` interface:

1. `SteamUGC.StartItemUpdate()` - Begin an update session
2. `SteamUGC.SetItemUpdateLanguage()` - Set the target language for description
3. `SteamUGC.SetItemDescription()` - Set the description for that language
4. `SteamUGC.SubmitItemUpdate()` - Submit all changes

This allows setting different descriptions for each language Steam supports.