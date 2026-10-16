namespace Genius.AppHost.Resources;

/// <summary>
/// The row that heads an application on the Resources page; its containers are its children. It runs
/// nothing itself. When the application's configuration is broken it is the only row, and says why.
/// </summary>
internal sealed class AppGroupResource(string name) : Resource(name);
