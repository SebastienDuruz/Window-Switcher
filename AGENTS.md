# AGENTS

But: fournir des consignes claires pour contribuer a Window Switcher en tant que developpeur Dotnet senior, en restant alignes avec les bonnes pratiques Microsoft.

## Stack et structure
- Solution: `Window-Switcher.sln`
- UI: `WindowSwitcher/` (Avalonia)
- Core/lib: `WindowSwitcherLib/`
- Tests: `WindowSwitcherTester/`
- Cible: `.NET 10` (`net10.0`)
- MVVM: `CommunityToolkit.Mvvm`

## Build, run, test (local)
- Build: `dotnet build Window-Switcher.sln`
- Run: `dotnet run --project WindowSwitcher/WindowSwitcher.csproj`
- Tests: `dotnet test WindowSwitcherTester/WindowSwitcherTester.csproj`

## Conventions de code (Microsoft)
- Nullability: garder `<Nullable>enable</Nullable>`; eviter les suppressions `!`; utiliser `ArgumentNullException.ThrowIfNull`.
- Async: privilegier `async/await`; ne jamais bloquer le thread UI; utiliser `Dispatcher.UIThread` pour les updates UI.
- Concurrence: utiliser `CancellationToken` sur les boucles longues; liberer les ressources (`IDisposable/Dispose`).
- MVVM: utiliser `ObservableObject` et `[ObservableProperty]` dans les ViewModels; garder le code-behind minimal.
- Config: passer par `ConfigFileAccessor` pour lecture/ecriture; eviter l'I/O direct depuis la UI.
- Logs: utiliser `AppLogger` et respecter `ActivateLogs`.
- Portabilite: isoler le code specifique OS dans `WindowSwitcherLib/Data/WindowAccess` (pas de P/Invoke dans la UI).
- Exceptions: fail-fast pour erreurs de programmation; gerer proprement les erreurs attendues (message utilisateur). 
- Collections: preferer `IReadOnlyCollection` en entree; eviter les mutations concurrentes.

## Qualite et tests
- Ajouter/ajuster des tests pour la logique non-UI dans `WindowSwitcherTester`.
- Documenter les API publiques de `WindowSwitcherLib` avec des commentaires XML.
