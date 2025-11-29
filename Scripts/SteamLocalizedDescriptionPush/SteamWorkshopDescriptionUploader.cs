using Steamworks;

namespace SteamLocalizedDescriptionPush;

/// <summary>
/// Updates Steam Workshop localized descriptions for DuravoQOLMod.
/// 
/// Prerequisites:
/// - Steam must be running and logged in as the mod owner
/// - The steam_appid.txt file must exist with tModLoader's AppId
/// 
/// Usage: dotnet run
/// </summary>
public static class SteamWorkshopDescriptionUploader
{
    // tModLoader's Steam App ID
    private const uint TModLoaderAppId = 1281930;

    // DuravoQOLMod Workshop item ID from: https://steamcommunity.com/sharedfiles/filedetails/?id=3614531050
    private const ulong DuravoModPublishedFileId = 3614531050;

    // Maps our locale codes to Steam's language API names
    // See: https://partner.steamgames.com/doc/store/localization/languages
    private static readonly Dictionary<string, string> LocaleToSteamLanguage = new()
    {
        { "de-DE", "german" },
        { "es-ES", "spanish" },
        { "fr-FR", "french" },
        { "it-IT", "italian" },
        { "pt-BR", "brazilian" },
        { "ru-RU", "russian" },
        { "pl-PL", "polish" },
        { "zh-Hans", "schinese" },
    };

    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== Steam Workshop Localized Description Updater ===");
        Console.WriteLine($"Target: DuravoQOLMod (Workshop ID: {DuravoModPublishedFileId})");
        Console.WriteLine();

        // Find the project root (look for description_workshop.txt)
        string? projectRoot = FindProjectRoot();
        if (projectRoot == null) {
            Console.Error.WriteLine("ERROR: Could not find project root (looking for description_workshop.txt)");
            Console.Error.WriteLine("Run this tool from within the DuravoQOLMod project directory.");
            return 1;
        }
        Console.WriteLine($"Project root: {projectRoot}");

        // Create steam_appid.txt for Steamworks initialization
        string steamAppIdPath = Path.Combine(AppContext.BaseDirectory, "steam_appid.txt");
        await File.WriteAllTextAsync(steamAppIdPath, TModLoaderAppId.ToString());

        // Initialize Steamworks
        if (!SteamAPI.Init()) {
            Console.Error.WriteLine("ERROR: Failed to initialize Steam API.");
            Console.Error.WriteLine("Make sure Steam is running and you're logged in as the mod owner.");
            return 1;
        }

        try {
            Console.WriteLine($"Steam initialized. Logged in as: {SteamFriends.GetPersonaName()}");
            Console.WriteLine();

            // Load all descriptions
            var localizedDescriptions = LoadLocalizedDescriptions(projectRoot);
            if (localizedDescriptions.Count == 0) {
                Console.Error.WriteLine("ERROR: No description files found!");
                return 1;
            }

            Console.WriteLine($"Found {localizedDescriptions.Count} description(s) to upload:");
            foreach (var kvp in localizedDescriptions) {
                string langDisplay = kvp.Key == "english" ? "english (primary)" : kvp.Key;
                Console.WriteLine($"  - {langDisplay}: {kvp.Value.Length} characters");
            }
            Console.WriteLine();

            // Submit each language as a SEPARATE update
            // Steam API only applies the last language set before SubmitItemUpdate,
            // so we need to submit each language individually
            int successCount = 0;
            int totalLanguages = localizedDescriptions.Count;
            
            foreach (var (steamLanguage, descriptionContent) in localizedDescriptions) {
                Console.WriteLine($"[{successCount + 1}/{totalLanguages}] Updating {steamLanguage}...");
                
                // Start a NEW update handle for each language
                var updateHandle = SteamUGC.StartItemUpdate(new AppId_t(TModLoaderAppId), new PublishedFileId_t(DuravoModPublishedFileId));
                
                if (!SteamUGC.SetItemUpdateLanguage(updateHandle, steamLanguage)) {
                    Console.Error.WriteLine($"  ERROR: Failed to set language to '{steamLanguage}'");
                    continue;
                }

                if (!SteamUGC.SetItemDescription(updateHandle, descriptionContent)) {
                    Console.Error.WriteLine($"  ERROR: Failed to set description for '{steamLanguage}'");
                    continue;
                }

                // Submit THIS language's update
                var submitCall = SteamUGC.SubmitItemUpdate(updateHandle, $"Updated {steamLanguage} description");
                var result = await WaitForCallResultAsync<SubmitItemUpdateResult_t>(submitCall);

                if (result.m_eResult == EResult.k_EResultOK) {
                    Console.WriteLine($"  ✓ {steamLanguage} updated successfully");
                    successCount++;
                } else {
                    Console.Error.WriteLine($"  ERROR: Steam returned {result.m_eResult} for {steamLanguage}");
                    if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement) {
                        Console.Error.WriteLine("  You need to accept the Steam Workshop legal agreement first.");
                    }
                }
                
                // Small delay between updates to avoid rate limiting
                await Task.Delay(500);
            }

            Console.WriteLine();
            if (successCount == totalLanguages) {
                Console.WriteLine($"SUCCESS! All {successCount} localized descriptions have been updated.");
            } else {
                Console.WriteLine($"Completed: {successCount}/{totalLanguages} languages updated.");
            }
            Console.WriteLine($"View at: https://steamcommunity.com/sharedfiles/filedetails/?id={DuravoModPublishedFileId}");
            return successCount == totalLanguages ? 0 : 1;
        }
        finally {
            SteamAPI.Shutdown();
        }
    }

    private static string? FindProjectRoot()
    {
        // Start from current directory and walk up looking for description_workshop.txt
        string? currentDir = Directory.GetCurrentDirectory();

        while (currentDir != null) {
            string descriptionPath = Path.Combine(currentDir, "description_workshop.txt");
            if (File.Exists(descriptionPath)) {
                return currentDir;
            }
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        return null;
    }

    private static Dictionary<string, string> LoadLocalizedDescriptions(string projectRoot)
    {
        var descriptions = new Dictionary<string, string>();

        // Load primary English description
        string primaryDescriptionPath = Path.Combine(projectRoot, "description_workshop.txt");
        if (File.Exists(primaryDescriptionPath)) {
            string englishContent = File.ReadAllText(primaryDescriptionPath);
            descriptions["english"] = englishContent;
        }

        // Load localized descriptions from Localization/description_workshop/
        string localizedDir = Path.Combine(projectRoot, "Localization", "description_workshop");
        if (Directory.Exists(localizedDir)) {
            foreach (string bbcodeFile in Directory.GetFiles(localizedDir, "*.bbcode")) {
                string fileName = Path.GetFileNameWithoutExtension(bbcodeFile);

                // Extract locale from filename: description_workshop-de-DE.bbcode -> de-DE
                if (fileName.StartsWith("description_workshop-")) {
                    string localeCode = fileName.Substring("description_workshop-".Length);

                    if (LocaleToSteamLanguage.TryGetValue(localeCode, out string? steamLanguage)) {
                        string localizedContent = File.ReadAllText(bbcodeFile);
                        descriptions[steamLanguage] = localizedContent;
                    }
                    else {
                        Console.WriteLine($"  WARNING: Unknown locale '{localeCode}' in {fileName}, skipping");
                    }
                }
            }
        }

        return descriptions;
    }

    private static Task<T> WaitForCallResultAsync<T>(SteamAPICall_t apiCall) where T : struct
    {
        var taskCompletionSource = new TaskCompletionSource<T>();

        // Create callback
        var callResult = CallResult<T>.Create((result, failure) => {
            if (failure) {
                taskCompletionSource.SetException(new Exception("Steam API call failed"));
            }
            else {
                taskCompletionSource.SetResult(result);
            }
        });

        callResult.Set(apiCall);

        // Run Steam callbacks until we get a result
        _ = Task.Run(async () => {
            while (!taskCompletionSource.Task.IsCompleted) {
                SteamAPI.RunCallbacks();
                await Task.Delay(100);
            }
        });

        return taskCompletionSource.Task;
    }
}