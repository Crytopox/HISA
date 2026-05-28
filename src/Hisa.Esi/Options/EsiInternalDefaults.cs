namespace Hisa.Esi.Options;

internal static class EsiInternalDefaults
{
    internal const string BaseUrl = "https://esi.evetech.net/latest/";
    internal const string UserAgent = "HISA/1.0 (+https://github.com/crytopox/hisa)";
    internal const string CompatibilityDate = "2026-05-28";

    internal const bool OAuthEnabled = false;
    internal const string AuthorizationEndpoint = "https://login.eveonline.com/v2/oauth/authorize";
    internal const string TokenEndpoint = "https://login.eveonline.com/v2/oauth/token";
    internal const string ClientId = "";
    internal const string ClientSecret = "";
    internal const string CallbackUrl = "";

    internal const int IncursionCacheSeconds = 300;
    internal const int IncursionTokenLimitPer15Minutes = 150;
    internal const int IncursionRefreshMinutes = 5;
}
