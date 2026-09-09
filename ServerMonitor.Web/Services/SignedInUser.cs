namespace ServerMonitor.Web.Services;

/// <summary>Who signed in, and the stamp their session is tied to.</summary>
public sealed record SignedInUser(string Username, string SecurityStamp);
