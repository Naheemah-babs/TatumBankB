using TatumWelcome2.Domain.Models;

namespace TatumWelcome2.Domain.Helper;

public static class NetworkHelper
{
    /// <summary>
    /// Turns whatever the caller typed into the official spelling from the
    /// supported networks list, ignoring case and surrounding spaces.
    /// "mtn" becomes "MTN", " GLO " becomes "Glo", "9MOBILE" becomes "9mobile".
    /// A name that isn't in the list is returned trimmed but otherwise unchanged,
    /// so the validator still rejects it with the normal invalid-network message.
    /// </summary>
    public static string Normalize(string? network)
    {
        // 1. A missing network becomes an empty string, and spaces are removed from both ends.
        string trimmed = network?.Trim() ?? string.Empty;

        // 2. Compare the input to each official name, ignoring upper/lower case.
        //    The first match is returned in the list's own spelling.
        string? match = DataPlans.Networks.FirstOrDefault(name =>
            string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase));

        // 3. No match means an unsupported network, so return what we have
        //    and let validation reject it.
        return match ?? trimmed;
    }
}