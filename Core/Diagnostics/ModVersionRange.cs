using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VortexLauncher.Core.Diagnostics;

// Conservative subset of Fabric predicates. null means unverified, never a hard mismatch.
internal static class ModVersionRange
{
    internal static bool? Matches(string? version, JsonElement range)
    {
        if (range.ValueKind == JsonValueKind.Array)
        {
            bool unknown = false; int count = 0;
            foreach (var alternative in range.EnumerateArray())
            {
                if (++count > 128) return null;
                var match = Matches(version, alternative);
                if (match == true) return true;
                unknown |= match == null;
            }
            return count == 0 || unknown ? null : false;
        }
        if (range.ValueKind != JsonValueKind.String) return null;
        string text = range.GetString()!.Trim();
        if (text == "*") return true;
        if (version == null || text.Length is 0 or > 512) return null;
        // Literal non-semantic versions are valid, but ranges/snapshots need Fabric's resolver.
        if (text == version) return true;
        var actual = Numbers(version);
        if (actual == null) return null;
        bool unknownPart = false, mismatch = false;
        foreach (var part in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(part, @"\A(>=|<=|>|<|=|~|\^)?([0-9xX*]+(?:\.[0-9xX*]+)*(?:\+[^\s]+)?)\z");
            if (!match.Success) { unknownPart = true; continue; }
            string op = match.Groups[1].Value, raw = match.Groups[2].Value;
            var pieces = raw.Split('+')[0].Split('.');
            int wildcard = Array.FindIndex(pieces, p => p is "x" or "X" or "*");
            if (wildcard >= 0)
            {
                if (op is not ("" or "=") || pieces.Skip(wildcard).Any(p => p is not ("x" or "X" or "*"))) { unknownPart = true; continue; }
                var prefix = wildcard == 0 ? Array.Empty<int>() : Numbers(string.Join('.', pieces.Take(wildcard)));
                if (prefix == null) { unknownPart = true; continue; }
                if (prefix.Where((n, i) => n != At(actual, i)).Any()) mismatch = true;
                continue;
            }
            var expected = Numbers(raw);
            if (expected == null) { unknownPart = true; continue; }
            int comparison = Compare(actual, expected);
            bool matches = op switch
            {
                ">=" => comparison >= 0, "<=" => comparison <= 0, ">" => comparison > 0, "<" => comparison < 0,
                "~" => comparison >= 0 && At(actual, 0) == At(expected, 0) && At(actual, 1) == At(expected, 1),
                "^" => comparison >= 0 && At(actual, 0) == At(expected, 0),
                _ => comparison == 0
            };
            if (!matches) mismatch = true;
        }
        return unknownPart ? null : !mismatch;
    }
    private static int[]? Numbers(string value)
    {
        value = value.Split('+')[0];
        if (!Regex.IsMatch(value, @"\A[0-9]+(?:\.[0-9]+){0,15}\z")) return null;
        var parts = value.Split('.'); var result = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) if (!int.TryParse(parts[i], out result[i])) return null;
        return result;
    }
    private static int At(int[] parts, int index) => index < parts.Length ? parts[index] : 0;
    private static int Compare(int[] a, int[] b)
    {
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++) { int c = At(a, i).CompareTo(At(b, i)); if (c != 0) return c; }
        return 0;
    }
}
