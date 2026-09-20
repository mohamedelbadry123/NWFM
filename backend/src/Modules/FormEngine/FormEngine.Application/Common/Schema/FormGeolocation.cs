using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FormEngine.Application.Common.Schema;

/// <summary>
/// A geolocation answer once it has been read out of whatever shape it arrived in. The address is
/// optional — a schema default carries none, a mobile client may not send one, and a point picked
/// somewhere the geocoder cannot name keeps its coordinates without it.
/// </summary>
public readonly record struct FormGeolocationPoint(double Latitude, double Longitude, string? Address);

/// <summary>
/// Reads and normalises a geolocation answer. The same point reaches the server in more than one
/// shape — the web builder posts a <c>{ "lat": …, "lng": …, "address": … }</c> object, a schema
/// default and a value read back out of the submissions column are plain text
/// (<c>"24.7136,46.6753"</c> or that object's JSON) — so every shape is handled here rather than at
/// each caller.
/// </summary>
public static class FormGeolocation
{
    private const string CoordinateFormat = "0.######";
    private const string Separator = ", ";
    private const char PairSeparator = ',';

    /// <summary>Between the coordinates and the address on the one line a human reads.</summary>
    private const string AddressSeparator = " — ";

    /// <summary>
    /// Caps the address so the canonical JSON always fits the answer's column. Kept in step with
    /// <c>MAX_ADDRESS_LENGTH</c> in the web app's <c>form-builder-payload.ts</c>.
    /// </summary>
    public const int MaxAddressLength = 250;

    private static readonly string[] LatitudeKeys = ["lat", "latitude"];
    private static readonly string[] LongitudeKeys = ["lng", "lon", "long", "longitude"];
    private static readonly string[] AddressKeys = ["address", "formatted_address", "formattedAddress"];

    private const double MaxLatitude = 90d;
    private const double MaxLongitude = 180d;

    /// <summary>Property names of the canonical write shape — see <see cref="ToJson"/>.</summary>
    private static class JsonKeys
    {
        public const string Latitude = "lat";
        public const string Longitude = "lng";
        public const string Address = "address";
    }

    /// <summary>
    /// Reads <paramref name="value"/> as a point. Returns <see langword="false"/> for anything that is
    /// not a readable coordinate, so the caller can fall back to the stored value — or reject the
    /// answer — instead of acting on a half-parsed point.
    /// </summary>
    public static bool TryRead(object? value, out FormGeolocationPoint point)
    {
        point = default;

        switch (value)
        {
            case null:
                return false;

            case JsonElement json:
                return TryFromJson(json, out point);

            case string raw:
                return TryFromText(raw, out point);

            default:
                return TryFromText(value.ToString(), out point);
        }
    }

    /// <summary>
    /// Renders <paramref name="value"/> as the one line a human reads —
    /// <c>"latitude, longitude"</c>, or <c>"latitude, longitude — address"</c> when the answer carries
    /// one. Returns <see langword="false"/>, leaving <paramref name="text"/> empty, for anything that
    /// is not a readable point.
    /// </summary>
    public static bool TryFormat(object? value, out string text)
    {
        if (!TryRead(value, out var point))
        {
            text = string.Empty;
            return false;
        }

        text = Describe(point);
        return true;
    }

    /// <summary>The point as the one line a human reads.</summary>
    public static string Describe(in FormGeolocationPoint point)
    {
        var coordinates = string.Concat(
            point.Latitude.ToString(CoordinateFormat, CultureInfo.InvariantCulture),
            Separator,
            point.Longitude.ToString(CoordinateFormat, CultureInfo.InvariantCulture));

        return string.IsNullOrWhiteSpace(point.Address)
            ? coordinates
            : string.Concat(coordinates, AddressSeparator, point.Address);
    }

    /// <summary>
    /// The canonical shape a geolocation answer is stored in:
    /// <c>{"lat":24.555838,"lng":46.385006,"address":"…"}</c>. Coordinates are trimmed to the display
    /// precision and the address is capped, so the result always fits the answer's column whatever the
    /// client posted.
    /// </summary>
    public static string ToJson(in FormGeolocationPoint point)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber(JsonKeys.Latitude, Round(point.Latitude));
            writer.WriteNumber(JsonKeys.Longitude, Round(point.Longitude));

            var address = Clamp(point.Address);
            if (address is not null)
            {
                writer.WriteString(JsonKeys.Address, address);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Trims to the address the store keeps, or <see langword="null"/> when there is none.</summary>
    private static string? Clamp(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var trimmed = address.Trim();
        return trimmed.Length <= MaxAddressLength ? trimmed : trimmed[..MaxAddressLength].TrimEnd();
    }

    /// <summary>Matches the precision <see cref="CoordinateFormat"/> renders, so both agree.</summary>
    private static double Round(double coordinate) => Math.Round(coordinate, 6, MidpointRounding.AwayFromZero);

    private static bool TryFromJson(JsonElement element, out FormGeolocationPoint point)
    {
        point = default;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                double? latitude = null;
                double? longitude = null;
                string? address = null;

                foreach (var property in element.EnumerateObject())
                {
                    if (address is null && Matches(property.Name, AddressKeys))
                    {
                        address = ReadText(property.Value);
                        continue;
                    }

                    if (!TryReadNumber(property.Value, out var number))
                    {
                        continue;
                    }

                    if (latitude is null && Matches(property.Name, LatitudeKeys))
                    {
                        latitude = number;
                    }
                    else if (longitude is null && Matches(property.Name, LongitudeKeys))
                    {
                        longitude = number;
                    }
                }

                return TryBuild(latitude, longitude, address, out point);

            case JsonValueKind.String:
                return TryFromText(element.GetString(), out point);

            default:
                return false;
        }
    }

    /// <summary>
    /// Handles both text shapes a stored point can take: the object's own JSON, and the
    /// <c>"lat,lng"</c> pair the form builder uses for a default value.
    /// </summary>
    private static bool TryFromText(string? raw, out FormGeolocationPoint point)
    {
        point = default;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();

        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                return TryFromJson(document.RootElement, out point);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        var parts = trimmed.Split(PairSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
        {
            return false;
        }

        return TryBuild(latitude, longitude, address: null, out point);
    }

    private static bool TryBuild(double? latitude, double? longitude, string? address, out FormGeolocationPoint point)
    {
        point = default;

        if (latitude is not { } lat || longitude is not { } lng)
        {
            return false;
        }

        if (double.IsNaN(lat) || double.IsNaN(lng) || Math.Abs(lat) > MaxLatitude || Math.Abs(lng) > MaxLongitude)
        {
            return false;
        }

        point = new FormGeolocationPoint(lat, lng, Clamp(address));
        return true;
    }

    /// <summary>A coordinate posted as a quoted string is still a coordinate, so both kinds are read.</summary>
    private static bool TryReadNumber(JsonElement element, out double number)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out number);

            case JsonValueKind.String:
                return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);

            default:
                number = 0;
                return false;
        }
    }

    /// <summary>An address is text; anything else on that key is ignored rather than stringified.</summary>
    private static string? ReadText(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static bool Matches(string name, string[] keys) =>
        keys.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
}
