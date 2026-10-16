namespace Genius.AppHost.Configuration;

/// <summary>
/// A problem in apps.json, an app.json or an app's .env, worded for whoever edits those files. The message
/// is shown as it is, in the app-host's log and on the Resources page, so it names the file and the field.
/// </summary>
internal sealed class ConfigurationException(string message) : Exception(message);
