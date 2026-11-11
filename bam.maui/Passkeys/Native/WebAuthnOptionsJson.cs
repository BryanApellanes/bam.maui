using System.Buffers.Text;
using System.Text.Json;

namespace Bam.Maui.Passkeys;

/// <summary>
/// Reads members out of WebAuthn options JSON.  Every failure is a
/// <see cref="PasskeyException"/> with <see cref="PasskeyErrorCategory.InvalidOptions"/> whose
/// message names the member and never quotes its value.
/// </summary>
internal static class WebAuthnOptionsJson
{
    internal const int MaxCredentialDescriptors = 64;
    internal const int MaxCredentialIdLength = 1023;

    internal static JsonDocument Open(string json, string what)
    {
        try
        {
            JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw Invalid($"{what} must be a JSON object.");
            }
            return document;
        }
        catch (JsonException exception)
        {
            // The parser's message quotes the input; only its type travels on.
            throw new PasskeyException(PasskeyErrorCategory.InvalidOptions, $"{what} is not valid JSON.", new FormatException(exception.GetType().Name));
        }
    }

    internal static PasskeyException Invalid(string message)
    {
        return new PasskeyException(PasskeyErrorCategory.InvalidOptions, message);
    }

    internal static JsonElement RequiredObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"The options have no '{name}' object.");
        }
        return value;
    }

    internal static string RequiredString(JsonElement parent, string name, string path)
    {
        string? value = OptionalString(parent, name, path);
        if (string.IsNullOrEmpty(value))
        {
            throw Invalid($"The options have no '{path}'.");
        }
        return value;
    }

    internal static string? OptionalString(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"'{path}' must be a string.");
        }
        return value.GetString();
    }

    internal static byte[] RequiredBytes(JsonElement parent, string name, string path, int maxLength)
    {
        string text = RequiredString(parent, name, path);
        byte[] bytes = DecodeBase64Url(text, path);
        if (bytes.Length == 0 || bytes.Length > maxLength)
        {
            throw Invalid($"'{path}' must decode to between 1 and {maxLength} bytes.");
        }
        return bytes;
    }

    internal static int? OptionalTimeout(JsonElement parent)
    {
        if (!parent.TryGetProperty("timeout", out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out long milliseconds) || milliseconds <= 0)
        {
            throw Invalid("'timeout' must be a positive whole number of milliseconds.");
        }
        return (int)Math.Min(milliseconds, int.MaxValue);
    }

    internal static IReadOnlyList<WebAuthnCredentialDescriptor> Descriptors(JsonElement parent, string name)
    {
        List<WebAuthnCredentialDescriptor> descriptors = new List<WebAuthnCredentialDescriptor>();
        if (!parent.TryGetProperty(name, out JsonElement list) || list.ValueKind == JsonValueKind.Null)
        {
            return descriptors;
        }
        if (list.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"'{name}' must be an array.");
        }
        if (list.GetArrayLength() > MaxCredentialDescriptors)
        {
            throw Invalid($"'{name}' lists more than {MaxCredentialDescriptors} credentials.");
        }
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"'{name}' must hold objects.");
            }
            // WebAuthn tells clients to ignore descriptors of a type they do not know.
            if (!string.Equals(OptionalString(item, "type", name + ".type"), WebAuthnCredentialDescriptor.PublicKeyType, StringComparison.Ordinal))
            {
                continue;
            }
            byte[] id = RequiredBytes(item, "id", name + ".id", MaxCredentialIdLength);
            descriptors.Add(new WebAuthnCredentialDescriptor(id, Strings(item, "transports", name + ".transports")));
        }
        return descriptors;
    }

    internal static IReadOnlyList<string> Strings(JsonElement parent, string name, string path)
    {
        List<string> values = new List<string>();
        if (!parent.TryGetProperty(name, out JsonElement list) || list.ValueKind == JsonValueKind.Null)
        {
            return values;
        }
        if (list.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"'{path}' must be an array.");
        }
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw Invalid($"'{path}' must hold strings.");
            }
            values.Add(item.GetString() ?? string.Empty);
        }
        return values;
    }

    internal static void RequireRelyingParty(string? actual, string expected, string path)
    {
        // A native app has no origin for the platform to derive an RP id from, so the options must name it, and it
        // must be the one this app was configured for: options for another relying party never reach the platform.
        if (string.IsNullOrEmpty(actual))
        {
            throw Invalid($"The options have no '{path}'.");
        }
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw Invalid($"'{path}' is not the relying party this app is configured for.");
        }
    }

    private static byte[] DecodeBase64Url(string text, string path)
    {
        // Base64Url.DecodeFromChars accepts padding and skips whitespace; WebAuthn JSON uses neither.
        foreach (char character in text)
        {
            bool allowed = char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_';
            if (!allowed)
            {
                throw Invalid($"'{path}' is not unpadded base64url.");
            }
        }
        try
        {
            return Base64Url.DecodeFromChars(text);
        }
        catch (FormatException)
        {
            throw Invalid($"'{path}' is not unpadded base64url.");
        }
    }
}
