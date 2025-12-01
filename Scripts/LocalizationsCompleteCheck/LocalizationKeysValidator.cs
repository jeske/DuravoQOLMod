using Hjson;

namespace LocalizationsCompleteCheck;

/// <summary>
/// Validates that all localization files have the same keys as the English reference file (en-US.hjson).
/// 
/// Uses the Hjson.Net NuGet package for proper HJSON parsing.
/// 
/// Usage: dotnet run
/// </summary>
public static class LocalizationKeysValidator
{
    private const string ReferenceLocale = "en-US";
    private const ConsoleColor ColorRed = ConsoleColor.Red;
    private const ConsoleColor ColorGreen = ConsoleColor.Green;
    private const ConsoleColor ColorYellow = ConsoleColor.Yellow;
    private const ConsoleColor ColorCyan = ConsoleColor.Cyan;

    public static int Main(string[] args)
    {
        Console.WriteLine();
        WriteColorLine(ColorCyan, "╔═══════════════════════════════════════════════════════════════╗");
        WriteColorLine(ColorCyan, "║         Localization Keys Validator                           ║");
        WriteColorLine(ColorCyan, "╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        // Find project root
        string? projectRoot = FindProjectRoot();
        if (projectRoot == null)
        {
            WriteColorLine(ColorRed, "ERROR: Could not find project root (looking for Localization/en-US.hjson)");
            return 1;
        }

        string localizationDir = Path.Combine(projectRoot, "Localization");
        string referenceFilePath = Path.Combine(localizationDir, $"{ReferenceLocale}.hjson");

        if (!File.Exists(referenceFilePath))
        {
            WriteColorLine(ColorRed, $"ERROR: Reference file not found: {referenceFilePath}");
            return 1;
        }

        Console.WriteLine($"Reference file: ");
        WriteColorLine(ColorCyan, $"  {referenceFilePath}");
        Console.WriteLine();

        // Extract reference keys using Hjson.Net
        HashSet<string> referenceKeys;
        try
        {
            referenceKeys = ExtractKeysFromHjson(File.ReadAllText(referenceFilePath));
        }
        catch (Exception ex)
        {
            WriteColorLine(ColorRed, $"ERROR: Failed to parse reference file: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Reference keys found: ");
        WriteColorLine(ColorCyan, $"  {referenceKeys.Count}");
        Console.WriteLine();

        // Get all other localization files
        string[] allHjsonFiles = Directory.GetFiles(localizationDir, "*.hjson");
        var otherLocaleFiles = allHjsonFiles
            .Where(f => !Path.GetFileName(f).Equals($"{ReferenceLocale}.hjson", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToArray();

        int totalErrors = 0;
        var fileResults = new List<(string FileName, List<string> MissingKeys, List<string> ExtraKeys)>();

        foreach (string localeFilePath in otherLocaleFiles)
        {
            string fileName = Path.GetFileName(localeFilePath);
            Console.Write($"Checking: ");
            WriteColor(ColorCyan, fileName);
            Console.Write("... ");

            HashSet<string> localeKeys;
            try
            {
                localeKeys = ExtractKeysFromHjson(File.ReadAllText(localeFilePath));
            }
            catch (Exception ex)
            {
                WriteColorLine(ColorRed, $"PARSE ERROR: {ex.Message}");
                totalErrors++;
                continue;
            }

            List<string> missingKeys = referenceKeys.Except(localeKeys).OrderBy(k => k).ToList();
            List<string> extraKeys = localeKeys.Except(referenceKeys).OrderBy(k => k).ToList();

            if (missingKeys.Count > 0 || extraKeys.Count > 0)
            {
                WriteColorLine(ColorYellow, "Issues found");
                fileResults.Add((fileName, missingKeys, extraKeys));
                totalErrors += missingKeys.Count + extraKeys.Count;
            }
            else
            {
                WriteColorLine(ColorGreen, $"OK ({localeKeys.Count} keys)");
            }
        }

        Console.WriteLine();

        // Display detailed issues
        if (fileResults.Count > 0)
        {
            WriteColorLine(ColorYellow, "═══════════════════════════════════════════════════════════════");
            WriteColorLine(ColorYellow, "                      ISSUES FOUND");
            WriteColorLine(ColorYellow, "═══════════════════════════════════════════════════════════════");

            foreach (var (fileName, missingKeys, extraKeys) in fileResults)
            {
                Console.WriteLine();
                WriteColor(ColorCyan, $"File: ");
                Console.WriteLine(fileName);

                if (missingKeys.Count > 0)
                {
                    WriteColorLine(ColorRed, $"  Missing keys ({missingKeys.Count}):");
                    foreach (string key in missingKeys)
                    {
                        Console.WriteLine($"    File ({fileName}) needs translation: {key}");
                    }
                }

                if (extraKeys.Count > 0)
                {
                    WriteColorLine(ColorYellow, $"  Extra keys ({extraKeys.Count}):");
                    foreach (string key in extraKeys)
                    {
                        Console.WriteLine($"    File ({fileName}) has extra key: {key}");
                    }
                }
            }

            Console.WriteLine();
            WriteColorLine(ColorRed, $"Total issues: {totalErrors}");
            return 1;
        }
        else
        {
            WriteColorLine(ColorGreen, "═══════════════════════════════════════════════════════════════");
            WriteColorLine(ColorGreen, "  All localization files are in sync with en-US.hjson!");
            WriteColorLine(ColorGreen, "═══════════════════════════════════════════════════════════════");
            Console.WriteLine();
            return 0;
        }
    }

    /// <summary>
    /// Extracts all leaf key paths from an HJSON file content using Hjson.Net library.
    /// </summary>
    private static HashSet<string> ExtractKeysFromHjson(string hjsonContent)
    {
        var keys = new HashSet<string>();

        // Parse HJSON to JSON value
        JsonValue rootValue = HjsonValue.Parse(hjsonContent);

        // Recursively extract all keys
        ExtractKeysRecursive(rootValue, "", keys);

        return keys;
    }

    /// <summary>
    /// Recursively traverses the JSON structure and collects all key paths.
    /// For objects, it descends into child keys.
    /// For leaf values (strings, numbers, etc.), it records the full path.
    /// </summary>
    private static void ExtractKeysRecursive(JsonValue jsonValue, string currentPath, HashSet<string> keys)
    {
        if (jsonValue is JsonObject jsonObject)
        {
            foreach (string key in jsonObject.Keys)
            {
                string fullPath = string.IsNullOrEmpty(currentPath) ? key : $"{currentPath}.{key}";
                JsonValue childValue = jsonObject[key];

                // Always add the key path (even for objects, they're meaningful in localization)
                keys.Add(fullPath);

                // If it's an object, recurse into it
                if (childValue is JsonObject)
                {
                    ExtractKeysRecursive(childValue, fullPath, keys);
                }
                // If it's an array, process array elements
                else if (childValue is JsonArray jsonArray)
                {
                    for (int arrayIndex = 0; arrayIndex < jsonArray.Count; arrayIndex++)
                    {
                        ExtractKeysRecursive(jsonArray[arrayIndex], $"{fullPath}[{arrayIndex}]", keys);
                    }
                }
                // Leaf value - already added above
            }
        }
    }

    private static string? FindProjectRoot()
    {
        // Start from current directory and walk up looking for Localization/en-US.hjson
        string? currentDir = Directory.GetCurrentDirectory();

        while (currentDir != null)
        {
            string localizationPath = Path.Combine(currentDir, "Localization", "en-US.hjson");
            if (File.Exists(localizationPath))
            {
                return currentDir;
            }
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        return null;
    }

    private static void WriteColor(ConsoleColor color, string text)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }

    private static void WriteColorLine(ConsoleColor color, string text)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = originalColor;
    }
}