using System.Globalization;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Hisa.Core.Models;

public static class XmppProfileParsing
{
    public static readonly XNamespace VCard = "vcard-temp";
    public static readonly XNamespace VCardUpdate = "vcard-temp:x:update";
    public static readonly XNamespace LastActivity = "jabber:iq:last";
    public const int MaxAvatarBytes = 256 * 1024;

    public static DateTimeOffset? LastSeenFromSeconds(string? seconds, DateTimeOffset now)
    {
        if (!ulong.TryParse(seconds, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return null;
        if (value > (ulong)TimeSpan.FromDays(3650).TotalSeconds) return null;
        return now - TimeSpan.FromSeconds(value);
    }

    public static (byte[]? Bytes, string? Type) ReadVCardPhoto(XElement? vcard)
    {
        var photo = vcard?.Element(VCard + "PHOTO") ?? vcard?.Elements().FirstOrDefault(element =>
            element.Name.LocalName.Equals("PHOTO", StringComparison.OrdinalIgnoreCase));
        if (photo is null) return (null, null);
        var type = (photo.Element(VCard + "TYPE") ?? photo.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("TYPE", StringComparison.OrdinalIgnoreCase)))
            ?.Value.Trim();
        var binval = (photo.Element(VCard + "BINVAL") ?? photo.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("BINVAL", StringComparison.OrdinalIgnoreCase)))
            ?.Value;
        if (string.IsNullOrWhiteSpace(binval)) return (null, null);
        try
        {
            var bytes = Convert.FromBase64String(string.Concat(binval.Where(c => !char.IsWhiteSpace(c))));
            if (bytes.Length is 0 or > MaxAvatarBytes) return (null, null);
            return (bytes, string.IsNullOrWhiteSpace(type) ? "image/jpeg" : type);
        }
        catch (FormatException)
        {
            return (null, null);
        }
    }

    public static string PhotoSha1Hex(byte[] bytes) =>
        Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();

    public static XElement BuildPhotoElement(byte[] bytes, string contentType) =>
        new(VCard + "PHOTO",
            new XElement(VCard + "TYPE", contentType),
            new XElement(VCard + "BINVAL", Convert.ToBase64String(bytes)));

    public static XElement ApplyPhoto(XElement? existingVCard, byte[]? bytes, string? contentType)
    {
        var vcard = existingVCard is null
            ? new XElement(VCard + "vCard")
            : new XElement(existingVCard);
        if (vcard.Name.Namespace != VCard &&
            !vcard.Name.LocalName.Equals("vCard", StringComparison.OrdinalIgnoreCase))
            vcard = new XElement(VCard + "vCard", vcard.Elements());
        else if (vcard.Name.Namespace != VCard)
            vcard.Name = VCard + "vCard";

        foreach (var photo in vcard.Elements().Where(element =>
                     element.Name.LocalName.Equals("PHOTO", StringComparison.OrdinalIgnoreCase)).ToArray())
            photo.Remove();

        if (bytes is { Length: > 0 } && !string.IsNullOrWhiteSpace(contentType))
            vcard.Add(BuildPhotoElement(bytes, contentType));
        return vcard;
    }
}
