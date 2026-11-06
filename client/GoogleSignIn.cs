using System;
using System.Threading.Tasks;
using Godot;

namespace ChaosIdle.Client;

/// <summary>
/// Google sign-in through the Android plugin (android/google-sign-in, singleton
/// "ChaosIdleGoogleSignIn"). It returns an ID token for our Web client id; the game server
/// checks it. On desktop and in CI there is no plugin, and <see cref="Unavailable"/> says why.
/// </summary>
public static class GoogleSignIn
{
    private const string Singleton = "ChaosIdleGoogleSignIn";

    /// <summary>From CHAOSIDLE_GOOGLE_CLIENT_ID or the project setting chaos_idle/auth/google_web_client_id.</summary>
    public static string ClientId =>
        System.Environment.GetEnvironmentVariable("CHAOSIDLE_GOOGLE_CLIENT_ID")
        ?? ProjectSettings.GetSetting("chaos_idle/auth/google_web_client_id", "").AsString();

    /// <summary>Why the Google button can't work in this build, or null when it can.</summary>
    public static string? Unavailable =>
        !Engine.HasSingleton(Singleton) ? "Google sign-in works in the Android app."
        : string.IsNullOrWhiteSpace(ClientId) ? "Google sign-in isn't set up in this build."
        : null;

    /// <summary>Shows Android's account sheet. Returns the ID token, or null and why not ("cancelled", ...).</summary>
    public static async Task<(string? IdToken, string Error)> SignInAsync()
    {
        if (Unavailable is { } why) return (null, why);
        var plugin = Engine.GetSingleton(Singleton);
        var done = new TaskCompletionSource<(string?, string)>();
        var ok = Callable.From((string token, string email) => done.TrySetResult((token, "")));
        var failed = Callable.From((string reason) => done.TrySetResult((null, reason)));
        plugin.Connect("sign_in_succeeded", ok);
        plugin.Connect("sign_in_failed", failed);
        try
        {
            plugin.Call("signIn", ClientId, false);
            return await done.Task;
        }
        finally
        {
            plugin.Disconnect("sign_in_succeeded", ok);
            plugin.Disconnect("sign_in_failed", failed);
        }
    }

    /// <summary>Forgets the chosen Google account on this phone (the next sign-in asks again).</summary>
    public static void SignOut()
    {
        if (Engine.HasSingleton(Singleton)) Engine.GetSingleton(Singleton).Call("signOut");
    }

    public static string Explain(string reason) => reason switch
    {
        "cancelled" => "Sign-in cancelled.",
        "no_account" => "No Google account on this phone. Add one in Android settings.",
        _ => "Google sign-in failed: " + reason,
    };
}
