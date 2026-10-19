namespace OpenStar.Core.Authentication;

/// <summary>
/// Values the myChevrolet mobile app uses against GM's Azure AD B2C tenant,
/// as documented by OnStarJS.
/// </summary>
public static class GmAuthConstants
{
    public const string ClientId = "3ff30506-d242-4bed-835b-422bf992622e";

    public const string RedirectUri = "msauth.com.gm.myChevrolet://auth";

    public const string Scope =
        $"https://gmb2cprod.onmicrosoft.com/{ClientId}/Test.Read openid profile offline_access";

    /// <summary>Host that serves GM's Azure AD B2C sign-in pages.</summary>
    public const string LoginHost = "custlogin.gm.com";

    public const string PolicyBaseUrl =
        $"https://{LoginHost}/gmb2cprod.onmicrosoft.com/b2c_1a_seamless_mobile_signuporsignin";

    public const string DiscoveryUrl = $"{PolicyBaseUrl}/v2.0/.well-known/openid-configuration";

    public const string FallbackAuthorizationEndpoint = $"{PolicyBaseUrl}/oauth2/v2.0/authorize";

    public const string FallbackTokenEndpoint = $"{PolicyBaseUrl}/oauth2/v2.0/token";

    public const string GmTokenPath = "/sec/authz/v3/oauth/token";

    public const string GmTokenScope = "onstar gmoc user_trailer user msso priv";

    public const string AppVersion = "myOwner-chevrolet-android-8.5.0-0";

    public const string UserAgent = "myOwner App";
}
