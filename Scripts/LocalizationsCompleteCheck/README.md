# Localization Keys Validator

A C# console application that validates all localization files have the same keys as the English reference file (`en-US.hjson`).

## Usage

From the project root (or within the Scripts/LocalizationsCompleteCheck directory):

```bash
dotnet run --project Scripts/LocalizationsCompleteCheck
```

Or build and run:

```bash
dotnet build Scripts/LocalizationsCompleteCheck
dotnet Scripts/LocalizationsCompleteCheck/bin/Debug/net8.0/LocalizationsCompleteCheck.dll
```

## What it Checks

1. **Missing Keys**: Keys that exist in `en-US.hjson` but are missing from a translation file
2. **Extra Keys**: Keys that exist in a translation file but not in `en-US.hjson` (usually indicates stale/removed keys)

## Output

- **Green**: File is in sync with the reference
- **Yellow**: File has extra keys (may need cleanup)
- **Red**: File is missing required keys

## Exit Codes

- `0`: All files are in sync
- `1`: Issues found (missing or extra keys)

## Example Output

```
╔═══════════════════════════════════════════════════════════════╗
║         Localization Keys Validator                           ║
╚═══════════════════════════════════════════════════════════════╝

Reference file: 
  Localization/en-US.hjson
Reference keys found: 
  95

Checking: de-DE.hjson... OK (95 keys)
Checking: es-ES.hjson... OK (95 keys)
Checking: fr-FR.hjson... Issues found
Checking: it-IT.hjson... OK (95 keys)
...

═══════════════════════════════════════════════════════════════
                      ISSUES FOUND
═══════════════════════════════════════════════════════════════

File: fr-FR.hjson
  Missing keys (1):
    - Mods.DuravoQOLMod.Configs.SomeNewKey
```

## Prerequisites

- .NET 8.0 SDK or later