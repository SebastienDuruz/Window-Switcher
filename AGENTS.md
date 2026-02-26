# AGENTS

But: fournir des consignes claires pour contribuer a Window Switcher en tant que developpeur Dotnet senior, en restant alignes avec les bonnes pratiques Microsoft.

## Stack et structure

- Solution: `Window-Switcher.sln`
- UI: `src/WindowSwitcher/` (Avalonia)
- Core/lib: `src/WindowSwitcher.Lib/`
- Tests: `src/WindowSwitcher.Tests/`
- Cible: `.NET 10` (`net10.0`)
- MVVM: `CommunityToolkit.Mvvm`

## Build, run, test (local)

- Build: `dotnet build Window-Switcher.sln`
- Run: `dotnet run --project src/WindowSwitcher/WindowSwitcher.csproj`
- Tests: `dotnet test src/WindowSwitcher.Tests/WindowSwitcher.Tests.csproj`

## Conventions de code (Microsoft)

- Nullability: garder `<Nullable>enable</Nullable>`; eviter les suppressions `!`; utiliser `ArgumentNullException.ThrowIfNull`.
- Async: privilegier `async/await`; ne jamais bloquer le thread UI; utiliser `Dispatcher.UIThread` pour les updates UI.
- Concurrence: utiliser `CancellationToken` sur les boucles longues; liberer les ressources (`IDisposable/Dispose`).
- MVVM: utiliser `ObservableObject` et `[ObservableProperty]` dans les ViewModels; garder le code-behind minimal.
- Config: passer par `ConfigFileAccessor` pour lecture/ecriture; eviter l'I/O direct depuis la UI.
- Portabilite: isoler le code specifique OS dans `src/WindowSwitcher.Lib/Data/WindowAccess` (pas de P/Invoke dans la UI).
- Exceptions: fail-fast pour erreurs de programmation; gerer proprement les erreurs attendues (message utilisateur).
- Collections: preferer `IReadOnlyCollection` en entree; eviter les mutations concurrentes.

## Qualite et tests

- Documenter les API publiques de `src/WindowSwitcher.Lib` avec des commentaires XML.
