using System.Runtime.CompilerServices;
using System.Windows;

// Brisk.Tests needs AppState.PendingConfirmTask (internal by design — a real
// join point for the test project, not part of the app's public surface).
[assembly: InternalsVisibleTo("Brisk.Tests")]

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
