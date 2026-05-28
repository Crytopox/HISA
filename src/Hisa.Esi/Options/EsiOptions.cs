namespace Hisa.Esi.Options;

public sealed class EsiOptions
{
    public string BaseUrl { get; set; } = "https://esi.evetech.net/latest/";
    public string UserAgent { get; set; } = "HISA/1.0";
    public string CompatibilityDate { get; set; } = "2026-05-28";
    public EsiOAuthOptions OAuth { get; set; } = new();
    public EsiIncursionsOptions Incursions { get; set; } = new();
}

public sealed class EsiOAuthOptions
{
    public bool Enabled { get; set; }
    public string AuthorizationEndpoint { get; set; } = "https://login.eveonline.com/v2/oauth/authorize";
    public string TokenEndpoint { get; set; } = "https://login.eveonline.com/v2/oauth/token";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public List<string> DefaultScopes { get; set; } = [];
}

public sealed class EsiIncursionsOptions
{
    public int CacheSeconds { get; set; } = 300;
    public int TokenLimitPer15Minutes { get; set; } = 150;
}
