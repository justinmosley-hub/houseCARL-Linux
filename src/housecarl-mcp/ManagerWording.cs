using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HousecarlMcp;

// houseCARL's words were written for MO2; for an Amethyst game the phrases that tell the user what to DO are Amethyst's.
// Contract in docs/architecture/amethyst-instance.md.
internal static class ManagerWording
{
    /// <summary>An MO2 phrase and Amethyst's in its place, most specific first so a general one never splits a specific
    /// one. Every MO2 side is verbatim from this server's source; <c>ManagerWordingTests</c> fails when one is not.</summary>
    internal static readonly (string Mo2, string Amethyst)[] AmethystPhrases =
    {
        // The standing instructions.
        ("via a live MO2 instance", "via an Amethyst game"),
        ("until the user enables its mod in MO2.", "until the user enables its mod and Deploys."),
        // A new mod folder: what makes it load.
        ("enable it in MO2 to use the patch; MO2 adds a newly activated plugin at the END of the load order",
         "refresh Amethyst, enable it and Deploy to use the patch; Amethyst adds a newly enabled plugin at the END of the load order"),
        ("then enable it in MO2 (MO2 adds a newly activated plugin at the END of the load order)",
         "then refresh Amethyst, enable it and Deploy (Amethyst adds a newly enabled plugin at the END of the load order)"),
        ("enable it in MO2", "refresh Amethyst, enable it and Deploy"),
        // Why a plugin is not loaded, and the remedy.
        ("MO2 has not registered that mod folder — refresh MO2, then tick the plugin and sort",
         "Amethyst has not listed that mod folder yet — refresh Amethyst, enable the mod, then Deploy"),
        ("which MO2 has not registered — refresh MO2, then tick the plugin and sort",
         "which Amethyst has not listed yet — refresh Amethyst, enable the mod, then Deploy"),
        ("switched OFF in MO2 — switch it on, then re-sort", "switched OFF in Amethyst — switch it on, then Deploy"),
        ("(MO2's right pane)", "(Amethyst's Plugins tab)"),
        ("is not registered in MO2's load order (refresh MO2 to pick it up)", "is not in Amethyst's load order (refresh Amethyst to pick it up)"),
    };

    /// <summary><paramref name="text"/> in <paramref name="manager"/>'s words: unchanged unless the manager is Amethyst. A
    /// json answer spells an apostrophe <c>\u0027</c> (<see cref="HousecarlCore.JsonTextEncoder"/>), so that spelling is
    /// swapped too.</summary>
    internal static string For(string? manager, string text)
    {
        if (manager != "Amethyst") return text;
        foreach (var (mo2, amethyst) in AmethystPhrases)
            text = text.Replace(mo2, amethyst, StringComparison.Ordinal)
                       .Replace(JsonSpelling(mo2), JsonSpelling(amethyst), StringComparison.Ordinal);
        return text;
    }

    static string JsonSpelling(string phrase) => phrase.Replace("'", "\\u0027", StringComparison.Ordinal);

    /// <summary>Every tool answer in the configured manager's words, read when the answer is made, so a manager set this
    /// session counts.</summary>
    public static McpRequestFilter<CallToolRequestParams, CallToolResult> AnswerWords => next => async (request, cancellationToken) =>
    {
        var result = await next(request, cancellationToken);
        var manager = request.Services?.GetService<LoadOrderService>()?.Manager;
        if (manager is null) return result;
        foreach (var block in result.Content)
            if (block is TextContentBlock text) text.Text = For(manager, text.Text);
        return result;
    };
}
