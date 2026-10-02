# Audit 0.14 — Performance et couverture des fonctionnalités

| | |
|---|---|
| Date | 2026-10-02 |
| Version auditée | 0.14.0 (`Directory.Build.props`) |
| Branche / commit | `feature/0.15` @ `e7400b0` |
| Périmètre | Totalité de `src/` (UI Avalonia, `WindowSwitcher.Lib`, adaptateur C PipeWire, tests) |
| Méthode | Lecture complète du code par zone, mesure de couverture (`dotnet test --collect "Code Coverage"`), micro-benchmark de `BgraFrameCopier` |

**Légende**

- **[V]** : vérifié dans le code.
- **[V✔]** : vérifié et relu une seconde fois lors de la consolidation.
- **[H]** : hypothèse à confirmer à l'exécution ou dépendante de la plateforme.
- Sévérités : **Critique** (blocage, coupure ou perte de fonctionnalité), **Élevée**, **Moyenne**, **Faible**.

---

## 1. Synthèse

L'architecture générale est saine. La capture et l'activation tournent hors du thread UI, les frames en attente sont regroupées (une seule frame en attente par aperçu), le catalogue de raccourcis est immuable et la liste principale est mise à jour de façon incrémentale.

L'audit relève cependant :

- **deux défauts critiques** : un blocage du thread UI sur le chemin DMA-BUF (C1) et des coupures répétées des aperçus X11 dès qu'ils sont plusieurs (C2) ;
- des chemins Linux qui **font beaucoup plus de travail que nécessaire** :
  - pas de limitation de cadence côté PipeWire ;
  - mise à l'échelle CPU en `double` ;
  - relecture pleine résolution sous X11 ;
  - interrogation périodique d'evdev ;
- une **couverture de tests qui ignore le cœur de l'application** : le cycle de vie des aperçus flottants est à 0 %, et il n'y a aucune CI.

| Indicateur | Valeur |
|---|---|
| Tests | 251, tous verts (exécution Linux, 0,5 s) |
| Couverture des lignes (global) | **44,6 %** (4 979 / 11 152) |
| `WindowSwitcher.Lib` | 54,8 % (4 071 / 7 430) |
| `WindowSwitcher` (UI) | 24,4 % (908 / 3 722) |
| Code C PipeWire | non couvert |
| CI | **aucune** (pas de `.github/`) |
| Tests de performance / benchmarks | aucun |

---

## 2. Constats critiques

### C1 — Blocage du thread UI à la destruction d'un flux DMA-BUF [V✔ mécanisme, non reproduit]

**Où**

- `src/WindowSwitcher.Lib/Data/Platform/WindowAccess/PreviewFrames/Pipewire/Native/window_switcher_pipewire.c:549-553`
- `src/WindowSwitcher/Windows/Services/FloatingWindowService.cs:143` (`Stop`) et `:340-384` (`ApplyFrameAsync`)
- `src/WindowSwitcher.Lib/Data/Platform/WindowAccess/PreviewFrames/Pipewire/PipeWireFrameProvider.Stream.cs:472` (`DestroyStream`)

**Mécanisme**

`destroy_capture` attend sans délai maximal que toutes les frames prêtées (*leases*) soient restituées :

```c
while (capture->outstanding_leases > 0)
    pthread_cond_wait(&capture->lease_condition, &capture->lease_mutex);
```

Cette destruction est appelée de façon synchrone depuis le thread UI, par deux chemins :

- `FloatingWindowClosing` → `Stop` → `ForgetWindow` → `PipeWireNativeStream.Dispose` → `DestroyStream` ;
- `ResetSelection` / `ResetAllSelections`, déclenchés depuis le menu principal.

Or `ApplyFrameAsync` détient un lease pendant qu'il attend `Dispatcher.UIThread.InvokeAsync(...)`. Le lease n'est restitué que dans le `finally`, une fois la closure exécutée sur le thread UI. Si le thread UI est bloqué dans `destroy_capture`, la closure ne s'exécute jamais. C'est un interblocage.

**Scénario le plus probable** : `Config > Reset all previews` avec au moins deux aperçus DMA-BUF.

1. Pendant la destruction du premier flux (arrêt et jointure du thread PipeWire, plusieurs dizaines de ms), le second flux continue de produire des frames.
2. Son `InvokeAsync` est mis en file sur le thread UI, qui est occupé.
3. La destruction du second flux attend alors un lease que seul le thread UI peut libérer.

**Portée** : uniquement le chemin DMA-BUF, c'est-à-dire Wayland avec `EGL_PLATFORM=x11` et un import EGL fonctionnel. Le chemin CPU rend le buffer PipeWire juste après la copie.

**Impact** : gel complet de l'application, qu'il faut tuer.

**Recommandation**

- Ne plus jamais attendre les leases de façon synchrone. Côté C, compter les références de `ws_pipewire_stream` : chaque lease tient une référence, et le dernier `ws_pipewire_frame_release` libère la structure.
- Ne pas bloquer le thread UI sur la destruction native (arrêt du thread loop) : la déporter en arrière-plan.

### C2 — X11 : stock de tampons partagé et épuisement interprété comme une perte de fenêtre [V✔]

**Où**

- `src/WindowSwitcher.Lib/Data/Platform/WindowAccess/PreviewFrames/X11/X11PreviewFrameProvider.cs:15` : `new NativeFrameBufferPool(maximumBuffers: 3)`, une seule instance pour tout le provider ;
- `X11FrameConverter.cs:30-32` : `TryRent` renvoie `null`, donc `CreateFrame` renvoie `null` ;
- `X11PreviewFrameProvider.cs:80-89` : une capture `null` entraîne `RemoveSession(windowId, session)` puis `yield break` ;
- `FloatingWindowService.cs:230` : `Task.Delay(500)`, puis création d'une nouvelle session (nouveau `XOpenDisplay`, nouveau XDamage, nouveau segment SHM).

**Mécanisme**

Chaque aperçu peut détenir jusqu'à trois leases à la fois : la frame en cours de capture, la frame en attente dans `_pendingFrame`, et la frame en cours d'application sur le thread UI. Avec deux aperçus ou plus, et un thread UI un peu chargé, le stock de 3 tampons est vide. Le provider ne distingue pas « stock vide » de « fenêtre disparue » et détruit la session.

À comparer avec PipeWire, où le stock de 3 tampons est créé **par flux** (`PipeWireFrameProvider.Stream.cs:57,63`).

**Impact** : avec 3 à 10 aperçus, coupures répétées d'au moins 500 ms, création et destruction en boucle de connexions X, de redirections Composite et de segments SHM. La fréquence réelle reste à mesurer.

**Recommandation**

- Un stock de tampons par session de capture.
- Distinguer « stock vide » (sauter la frame et réessayer au prochain dommage) de « capture impossible » (fin du flux).
- Ajouter un test de non-régression avec plusieurs sessions et des leases détenus.

---

## 3. Constats élevés — performance

### P1 — PipeWire : la cible de 20 FPS n'est appliquée nulle part [V code, H comportement du compositeur]

- `window_switcher_pipewire.c:168, 205-207` : seul `SPA_FORMAT_VIDEO_framerate` est borné, et `SPA_FORMAT_VIDEO_maxFramerate` est absent. Mutter, KWin et xdg-desktop-portal-wlr annoncent `framerate=0/1` (cadence variable) et se limitent d'après `maxFramerate`.
- `on_process` (`c:383`) n'applique aucune limitation.
- Côté consommateur non plus : `PreviewFrameTiming` n'est utilisé que par X11 (`X11PreviewFrameProvider.cs:99`).
- Le README affirme pourtant une « cible uniforme de 20 FPS ».

**Impact** : un jeu à 144 Hz qui redessine à chaque frame envoie 144 frames/s par aperçu à travers toute la chaîne (mise à l'échelle, dispatcher, import EGL, `glFinish`). Avec 10 aperçus, cela fait 1 440 frames/s.

**Recommandation**

- Ajouter `SPA_FORMAT_VIDEO_maxFramerate` en `CHOICE_RANGE(20/1, 0/1, 20/1)`.
- Ajouter une limitation dans `on_process` (horloge monotone ; si moins de 50 ms se sont écoulées, remettre immédiatement le buffer en file sans passer par le code managé).
- Conserver le dernier buffer ignoré pour ne pas figer l'aperçu sur un contenu périmé.

### P2 — Mise à l'échelle bilinéaire CPU scalaire en `double` [V + mesuré]

- `BgraFrameCopier.cs:190-248` : pour chaque pixel et chaque canal, la boucle appelle `MapInputChannel` (branchements), `Interpolate` en `double` avec `Math.Round` et `Clamp`, et passe par un indexeur `IReadOnlyList<>` (appel d'interface).
- Mesures (micro-benchmark local) :

| Source → cible | Coût par frame |
|---|---|
| 1080p → 640×360 | 3,0 ms |
| 1440p → 960×540 | 5,6 ms |
| Même taille (copie) | 0,09 ms |

- Ce travail s'exécute sur le thread PipeWire, avec le verrou de la boucle tenu.
  - 20 FPS × 10 aperçus ≈ 0,6 cœur ;
  - 60 FPS × 5 aperçus ≈ 0,9 cœur ;
  - 144 FPS × 10 aperçus ≈ 4,3 cœurs.
- [H] C'est probablement le chemin par défaut : `Program.cs:49-65` place Vulkan en premier tant que `EGL_PLATFORM` n'est pas défini, donc l'import DMA-BUF n'est pas disponible.

**Recommandation**

- Virgule fixe entière, tableaux `BgraScaleAxisPoint[]`, branche de format sortie de la boucle, `Vector128`.
- Ou déplacer la mise à l'échelle dans le C (`-O3`, filtre box façon libyuv), ce qui évite aussi le rappel vers le code managé.
- Ignorer le canal alpha : le bitmap est `AlphaFormat.Opaque` (`FloatingWindowService.cs:465`).

### P3 — DMA-BUF : `glFinish` et nouvelle EGLImage à chaque frame [V code, H thread]

- `DmaBufPreviewControl.cs:154` : `gl.Finish()` à chaque frame. Cela attend une file GPU partagée avec les jeux, soit 0,5 à plus de 10 ms par aperçu.
- `DmaBufPreviewControl.cs:202` et `LinuxDmaBufEglInterop.cs:122-137` : `eglCreateImageKHR` / `eglDestroyImageKHR` à chaque frame.
- `DmaBufPreviewControl.cs:213` : `GetUniformLocationString` à chaque frame (allocation et conversion de chaîne), plus 4 `TexParameteri`.
- [H] Avec Avalonia, `OnOpenGlRender` s'exécute sur le thread UI.

**Recommandation**

- `glFenceSync` et libération différée du lease, vérifiée par `glClientWaitSync(…, 0)` à la frame suivante.
- Cache d'EGLImage par buffer PipeWire (identifiant et génération transmis par le C, invalidés sur `param_changed` / `remove_buffer`).
- Uniform et paramètres de texture fixés dans `OnOpenGlInit`.

### P4 — X11 : relecture pleine résolution à chaque frame [V]

- `X11WindowCaptureSession.cs:432-444` : `XShmGetImage` sur tout le pixmap, puis `XSync`.
- `:275` : `XGetWindowAttributes` à chaque frame, soit un aller-retour de plus.
- La réduction se fait ensuite sur CPU, en plus proche voisin, avec deux divisions 64 bits par pixel (`X11FrameConverter.cs:199-250`).
- Ordre de grandeur pour une source 1080p : 8 Mo × 20 FPS ≈ 165 Mo/s par aperçu, 1,6 Go/s pour 10 aperçus. Avec glamor, c'est une relecture GPU vers CPU bloquante.
- En repli `XGetImage` (`:470`), Xlib alloue une image complète et la transfère par le socket à chaque frame.
- Chaque session garde un segment SHM pleine taille, soit 80 à 150 Mo pour 10 aperçus.

**Recommandation**

- Réduire côté serveur : `XRenderSetPictureTransform` avec filtre bilinéaire vers un pixmap à la taille de l'aperçu, puis `XShmGetImage` de ce petit pixmap (gain estimé de 10× à 25×).
- Mettre la géométrie en cache via `ConfigureNotify`.

### P5 — Linux : lecture evdev par interrogation à 5 ms [V✔, latence estimée]

- `LinuxGlobalKeyboardListener.cs:548-552` : sur `EAGAIN`, la lecture fait `await Task.Delay(5 ms)`.
- Une boucle asynchrone par périphérique sur le ThreadPool (l.374-383), puis un `Channel` vers le processeur (l.605-635), puis `write(/dev/uinput)`.

**Impact** : **toutes** les frappes, y compris hors raccourci (WASD), prennent 0 à environ 5-6 ms de latence en plus (environ 2,5-3 ms en moyenne), avec une gigue qui dépend de la charge du ThreadPool, donc des aperçus. Au repos, chaque nœud clavier saisi provoque 200 réveils par seconde, y compris les nœuds clavier des souris « gaming ».

**Recommandation** : thread dédié à priorité élevée, `poll`/`epoll` bloquant sur tous les fd avec un `eventfd` d'arrêt, traitement en ligne (filtre puis uinput), une seule écriture par trame `SYN`.

### P6 — Écritures de config synchrones, sans regroupement et inconditionnelles [V✔]

- `ConfigFileAccessor.UpdateConfig` (`:270-285`) incrémente la version et réécrit **toujours** le fichier, même quand le lambda ne change rien (cas de `SettingsViewModel.cs:185-191`).
- Chaque écriture comprend une sérialisation Newtonsoft complète (indentée), un fichier temporaire puis `File.Move`, sur le thread appelant (le thread UI).
- `_fileWriteGate.Wait()` (`:341`) est une attente bloquante, interdite par `AGENTS.md`.
- `UpdateConfigAsync` existe mais n'est appelée nulle part.
- `NumericUpDown` et `ColorPicker` sont liés en TwoWay (`SettingsWindow.axaml:56,68,83`). Chaque valeur déclenche une écriture disque, `AccentColorApplier.Apply` (re-style global) et `ApplySettings` sur tous les aperçus (`SettingsViewModel.cs:144-163`).
- À la fermeture, `CloseAll` fait N écritures complètes, une par aperçu.

**Recommandation** : `UpdateConfigAsync`, regroupement des écritures (300-500 ms), aucune écriture si rien n'a changé, `WaitAsync`, une seule écriture groupée à la fermeture.

### P7 — Activation par raccourci : énumération complète à chaque appui [V, coût estimé]

- `WindowKeybindActivator.cs:82-92, 131-167` : `GetWindowsAsync` à chaque appui.
  - Windows : `EnumWindows`, `GetWindowText` par fenêtre, `Process.GetProcessById` par PID, deux `Task.Run`.
  - X11 : 2 à 4 `XGetWindowProperty` par fenêtre, sous un `_operationGate` partagé avec le rafraîchissement toutes les 250 ms (`X11EwmhWindowAccessor.cs:33,160`). Un appui peut donc attendre la fin d'un passage de rafraîchissement.
- Concurrence : deux « suivant » rapides lisent la même ancre (l.142) avant sa mise à jour après l'`await` (l.164). Ils visent la même fenêtre et un appui est perdu.
- Pas de file d'activation, ni de timeout, ni de `CancellationToken` (`GlobalWindowKeybindRuntimeService.cs:189`).

**Recommandation** : file mono-consommateur, ancre mise à jour de façon synchrone au moment de l'appui, cache de fenêtres alimenté par le rafraîchissement existant, activation directe par handle.

---

## 4. Constats élevés — comportement

### B1 — Un échec d'énumération X11 ferme tous les aperçus [V✔]

- `X11EwmhWindowAccessor.cs:84-88` renvoie `[]` sur exception.
- `X11EwmhClient.cs:25,49,57,94` renvoie `[]` si la connexion échoue, si `_NET_CLIENT_LIST` manque ou si le format est inattendu.
- `WindowListViewModel.cs:138-145` retire alors toutes les fenêtres absentes, ce qui ferme chaque aperçu (`FloatingWindowRegistry.Remove` → `Close` → `Save()`).

**Impact** : un échec passager provoque N fermetures et recréations d'aperçus, N écritures synchrones de la config et, sous Wayland, peut-être une nouvelle demande de sélection au portail [H].

**Recommandation** : résultat typé (succès ou échec), conserver le snapshot précédent en cas d'échec, ne retirer une fenêtre qu'après K passages consécutifs d'absence.

### B2 — Config illisible écrasée par les valeurs par défaut, sans sauvegarde [V]

- `ConfigFileAccessor.cs:63-86` écrase le fichier sur `JsonException`, mais aussi sur `IOException` ou `UnauthorizedAccessException` passagères (antivirus, outil de synchronisation).
- `"WindowTitle": null` provoque une `NullReferenceException` dans le setter (`WindowConfig.cs:20`), ce qui déclenche aussi la réinitialisation.
- Si l'écriture des valeurs par défaut échoue, le `Lazy` (`:10`) mémorise l'exception et `GetInstance()` échoue à chaque appel : l'application plante au démarrage.

**Impact** : perte des filtres, des positions et des raccourcis.

**Recommandation** : renommer en `config.json.corrupt-<horodatage>` ; réinitialiser uniquement sur erreur JSON ; sur erreur d'I/O, réessayer sans réécrire ; ne pas mémoriser l'exception.

### B3 — Windows : raccourcis inopérants en AZERTY / QWERTZ [V✔ code, à confirmer par un test]

- La capture privilégie `PhysicalKey` d'Avalonia, c'est-à-dire la position physique avec des noms QWERTY (`AvaloniaKeybindCaptureMapper.cs:100`).
- À l'exécution, Windows résout le code VK, qui dépend de la disposition du clavier (`GlobalKeyEventKeyResolver.cs:128-204`).
- Résultat : A/Q, Z/W, Y/Z, M et les touches OEM sont permutées, et le raccourci ne se déclenche jamais. Linux reste cohérent, car evdev donne la position physique.
- NumPadEnter est impossible sous Windows (drapeau étendu ignoré, l.163). Sous Linux, `KEY_COMPOSE` (Menu) et `KeypadEqual` ne sont pas mappées.

**Recommandation** : résoudre la touche Windows depuis `scanCode` et `LLKHF_EXTENDED` ; ajouter des tests de correspondance capture ↔ exécution par plateforme.

### B4 — Correctif e7400b0 incomplet sous Windows [V code, H comportement de l'OS]

Ce que le correctif règle [V] :

- les modificateurs sont toujours transmis immédiatement (`GlobalWindowKeybindRuntimeService.cs:105-110`) ;
- la touche principale est consommée du Down au Up, répétitions comprises (l.158-170), ce qui est testé ;
- le filtre est réinitialisé quand une génération de périphériques Linux s'arrête (`LinuxGlobalKeyboardListener.cs:440`).

Ce qui manque :

- **Modificateur « tapé » seul** : la touche principale est avalée (`WindowsGlobalKeyboardListener.cs:270-272`), mais Windows voit Win↓ puis Win↑ et ouvre le menu Démarrer. Alt↓ puis Alt↑ fait passer la fenêtre en mode menu (`SC_KEYMENU`). Le commit a supprimé `SendInput`, donc il n'existe plus de masquage.
- **Relâchements perdus** : sur le bureau sécurisé (Win+L, Ctrl+Alt+Suppr, UAC) ou quand une fenêtre élevée a le focus (UIPI), le hook ne reçoit pas les Up. `_pressedModifiers` (runtime l.19, 208-217) garde alors Ctrl, Alt ou Meta. Toutes les combinaisons sont fausses jusqu'au prochain tap du modificateur. Un jeu lancé en administrateur rend les raccourcis inopérants.
- **Entrée périmée dans `_consumedPrimaryKeys`** : après un Up perdu, le Down suivant est vu comme une répétition (`_pressedKeys`, l.224) et la frappe est avalée sans action.
- [H] Alt↓ est livré à la fenêtre A et Alt↑ à la fenêtre B après l'activation : modificateur collé dans certains jeux.
- Tous les tests du runtime utilisent des événements Windows (`VK_xx`). Le chemin Linux (`KeyName`) n'est jamais testé.

**Recommandation** :

- touche masque (VK `0xE8`) via `SendInput` quand une touche est consommée avec Alt ou Win tenu (ignorée par le hook grâce à `LLKHF_INJECTED`) ;
- modificateurs reconstruits via `GetAsyncKeyState` à chaque Down de touche principale ;
- Reset sur `WM_WTSSESSION_CHANGE` ;
- purge de `_consumedPrimaryKeys` à chaque Down non répété.

### B5 — Linux : processus figé, claviers morts [V/H]

- `EVIOCGRAB` (l.489) coupe aussi les gestionnaires VT et SysRq.
- Un crash est sans danger : le noyau ferme les fd, relâche le grab et détruit uinput.
- En revanche, un processeur bloqué (interblocage, pause GC, point d'arrêt) ne déclenche la sécurité qu'après saturation du `Channel` de 2 048 éléments (l.99-106) plus 250 ms (l.587-597), soit environ 340 frappes « à l'aveugle ».

**Recommandation** : watchdog sur l'horodatage de progression du processeur (au-delà de 250 ms, `EVIOCGRAB 0`) et `Channel` d'au plus 64 éléments.

### B6 — Exceptions attendues qui font planter l'application ; crashs probablement perdus pour Sentry [V code, H SDK]

- `MainWindow.axaml.cs:235` : `Process.Start` sans `try/catch` (`Open data folder`). Échoue si `xdg-open` est absent ou bloqué par une sandbox.
- `ConfigFileAccessor.cs:428` : `File.Move` peut lever une `IOException`, qui remonte depuis l'ajout de préfixe, `Reset`/`Clean` et `OnClosing`.
- `GitHubAppUpdateService.cs:92-94` : `ReadAsStringAsync` est hors du `try`. Le timeout de `HttpClient` lève une `TaskCanceledException`, relancée aux lignes 68-71. Avec `AsyncRelayCommand` (`CheckForUpdatesCommand`), cela plante le thread UI.
- Côté Sentry :
  - `DisableAppDomainProcessExitFlush()` (`SentryAppTelemetry.cs:320`) ;
  - captures manuelles sans `Flush` (`:123-139`) ;
  - `OnDispatcherUnhandledException` ne positionne pas `Handled` (`App.axaml.cs:222`) ;
  - `OnDesktopExit` est en `async void` (`:185`).

  La règle « toute exception non gérée remonte à Sentry » n'est probablement pas respectée en pratique.

**Recommandation** : capturer ces erreurs attendues et les traduire en messages utilisateur ; `SentrySdk.Flush(2 s)` synchrone dans les gestionnaires terminaux, ou `CacheDirectoryPath`.

---

## 5. Constats moyens

### Aperçus

| ID | Constat | Où | Statut |
|---|---|---|---|
| M1 | Rendu OpenGL inutile à chaque frame CPU : sur tout Linux, le contrôle DMA-BUF est visible, et `ClearFrame()` déclenche `RequestNextFrameRendering()`. Jusqu'à 200 rendus GL vides par seconde pour 10 aperçus, plus un contexte GL inutilisé par fenêtre. | `FloatingWindow.axaml.cs:122`, `FloatingWindowService.cs:366`, `DmaBufPreviewControl.cs:70-79` | [V] |
| M2 | La capture continue quand l'aperçu est minimisé ou masqué. `SuspendWindow` n'est appelé que lorsque les aperçus sont désactivés. | `FloatingWindowService.cs:165,401` | [V] |
| M3 | X11 : aperçu figé après minimisation et restauration, ou changement de bureau. Le pixmap n'est renommé que si la taille change, alors que le serveur réalloue le stockage à chaque *map*. | `X11WindowCaptureSession.cs:281-282` | [H forte] |
| M4 | X11 : `XPending` interrogé toutes les 33 ms, avec une connexion X par session. Cela fait 30 réveils par seconde par aperçu et jusqu'à 33 ms de latence en plus. | `X11WindowCaptureSession.cs:143-160` | [V] |
| M5 | X11 : réessais sans recul progressif (fenêtre non mappée, plein écran dé-redirigé). Sans compositeur, l'alternance `XCompositeRedirectWindow` / `Unredirect` peut faire scintiller le jeu. | `X11WindowCaptureSession.cs:88-97,182-188` | [V/H] |
| M6 | PipeWire : leases DMA-BUF conservés pendant une renégociation (pas de gestionnaire `remove_buffer`). Peut faire échouer l'import EGL, puis basculer définitivement vers le CPU. | `c:511-516,716-763` | [H] |
| M7 | PipeWire : nombre de buffers non négocié, alors que le consommateur peut détenir jusqu'à 4 leases. Saccades possibles avec un producteur à 2 ou 3 buffers. | `c:340-344` | [H] |
| M8 | PipeWire : recadrage appliqué en décalant l'offset du DMA-BUF, ce qui n'est valide qu'avec un modifier linéaire. | `c:438-452` | [V] |
| M9 | Un passage par le dispatcher par frame, à priorité Normale (au-dessus d'Input). | `FloatingWindowService.cs:351` | [V] |
| M10 | Windows DWM : miniature enregistrée dans `Task.Run`, sans synchronisation avec `OnWindowResized` (miniature orpheline possible). Désenregistrement et réenregistrement à chaque redimensionnement. Échelle tirée de `Screens.Primary`. Résultat de `DwmUpdateThumbnailProperties` ignoré. | `FloatingWindowService.cs:43,87,94-95,181-185,582`, `WindowsDwmNativeThumbnailRenderer.cs:53` | [V/H] |
| M11 | Destruction d'une session X11 synchrone sur le thread UI : attente de `_sync` pendant une capture (5 à 30 ms) puis `XCloseDisplay`. | `FloatingWindowService.cs:143,165` | [V] |
| M12 | Création des flux PipeWire sérialisée sous `_portalCreationGate`, avec jusqu'à 2 attentes de 10 s par flux. L'annulation est ignorée dans `Task.Run`. Un `pw_thread_loop` et un `pw_context` par flux. | `PipeWireFrameProvider.cs:315-375`, `c:28,647-693` | [V] |

### Liste des fenêtres et configuration

| ID | Constat | Où | Statut |
|---|---|---|---|
| M13 | Coût du rafraîchissement toutes les 250 ms. X11 : environ 3N `XGetWindowProperty` synchrones (`_NET_WM_VISIBLE_NAME` presque toujours absent), soit environ 480 allers-retours par seconde pour 40 fenêtres, y compris dans le tray. Windows : `Process.GetProcessById` pour chaque PID à chaque passage, sans cache. Thread UI : clone complet de la config 4 fois par seconde. | `WindowListViewModel.cs:15`, `X11EwmhClient.cs:262-269`, `WindowsWinAccessor.cs:20-21,121`, `ConfigFileWindowFilterSettingsProvider.cs:14` | [V] |
| M14 | `ReadConfig` clone toute la config (fenêtres sauvegardées et raccourcis) à chaque survol ou clic sur un aperçu. | `FloatingWindowSettingsService.cs:37-45`, `ConfigFileAccessor.cs:260` | [V] |
| M15 | Démarrage synchrone sur le thread UI : conteneur DI, lecture de la config, `SentrySdk.Init`, sondage du provider d'aperçu, et création immédiate de 6 fenêtres (dont Settings avec `ColorPicker`). | `App.axaml.cs` | [V] |

### Fonctionnel

| ID | Constat | Où | Statut |
|---|---|---|---|
| M16 | Blacklist : l'ajout est refusé si une entrée existante **commence** par le titre (`StartsWith`), alors que le filtre compare des titres exacts. La fenêtre reste affichée. | `WindowBlacklistCoordinator.cs:26`, `PrefixListService.cs:32-34`, `WindowListViewModel.cs:107` | [V✔] |
| M17 | Titre de l'aperçu jamais resynchronisé : `FloatingWindow.WindowConfig` est un clone distinct. « Add to blacklist » depuis l'aperçu et la clé de position utilisent un titre périmé. | `FloatingWindow.axaml.cs:113,119,128` | [V] |
| M18 | La taille fixe écrase la taille propre à chaque aperçu : `Resized` recopie la taille quelle que soit la `Reason`. | `FloatingWindow.axaml.cs:308-311,381-386` | [V] |
| M19 | `Generate new config file` et `Clean config file` laissent un état incohérent : `PrefixListService` garde ses copies, `SettingsViewModel` n'est pas notifié, `Clean` est annulé à la fermeture par `CloseAll` → `Save`. Aucune confirmation n'est demandée. | `MainWindow.axaml.cs:75-78` | [V] |
| M20 | La surbrillance, « client actif » et l'ancre de cycle ne suivent pas le focus réel de l'OS : Alt+Tab et les clics directs dans le jeu sont ignorés. | `FloatingPreviewCoordinator`, `WindowKeybindActivator` | [V] |
| M21 | Tray : minimiser masque toujours dans le tray. Sous GNOME sans extension de zone de notification, la fenêtre devient irrécupérable. Une seconde instance quitte silencieusement sans réactiver la première. | `MainWindow.axaml.cs:136,194-195`, `Program.cs:18-25` | [H] |

### Clavier

| ID | Constat | Où | Statut |
|---|---|---|---|
| M22 | Hot-plug Linux : tout changement de périphérique arrête tous les lecteurs, détruit et recrée uinput, ce qui relâche les touches tenues sur les autres claviers. Pas d'`EVIOCGKEY` avant le grab : une touche tenue au moment du grab reste collée. Détection par sondage toutes les 2 s. | `LinuxGlobalKeyboardListener.cs:20,309-321,409-441` | [V/H] |
| M23 | Modificateurs gauche/droite et multi-claviers fusionnés dans un `HashSet<KeybindModifier>`. | `GlobalKeyEventKeyResolver.cs:38-41,82-85` | [V] |
| M24 | AltGr : vu comme Ctrl+Alt sous Windows et comme Alt sous Linux. Un raccourci Ctrl+Alt+X avale `@`, `€` et `#` en AZERTY. | — | [V/H] |
| M25 | Pendant la capture d'un raccourci dans l'UI, le filtre global reste actif : presser un raccourci existant change de fenêtre. | `KeybindSettingsViewModel` | [V] |
| M26 | La bannière de permissions n'est évaluée qu'au démarrage. Les pannes ultérieures (perte de permission, échec uinput, `EVIOCGRAB` en `EBUSY`) ne laissent qu'une trace, et `IsRunning` reste vrai. | `App.axaml.cs:148-167`, `GlobalKeyboardStartupStatusService.cs:242-252` | [V] |
| M27 | Nœuds combinés clavier + pointeur saisis en exclusif : `EV_REL`, `EV_ABS` et `EV_MSC` sont jetés, donc le pointeur intégré ne marche plus et les LED ne se mettent plus à jour. | `LinuxGlobalKeyboardListener.cs:648`, `LinuxUinputKeyboardForwarder.cs:98-113` | [V/H] |
| M28 | Hook Windows : thread à priorité normale, aucune détection d'une désinstallation silencieuse (`LowLevelHooksTimeout`), `GetKeyNameText` et formatage de chaînes dans le callback. | `WindowsGlobalKeyboardListener.cs:61-65,229,275-288` | [V/H] |

### Conformité à `AGENTS.md`

| ID | Constat | Où |
|---|---|---|
| M29 | Plus de 25 `catch { }` silencieux. Le plus grave : `WindowListViewModel.cs:172`, qui avale aussi les erreurs des gestionnaires `CollectionChanged`, donc les échecs de création d'aperçu. Une quinzaine dans `X11WindowCaptureSession`. Les autres sont dans `FloatingWindowService.cs:233-236`, `FloatingWindow.axaml.cs:291-294`, `App.axaml.cs:182,196,213`, `StartupUpdateNotificationService.cs:42`, `WindowsWinAccessor.cs:51,124`, `ConfigFileAccessor.cs:438`, le runtime clavier et le listener Windows. |
| M30 | `TracePlatformDiagnostics` écrit via `Trace` sans aucun listener : les diagnostics ne vont nulle part en production. Seul le type de l'exception est conservé. |
| M31 | `XSetErrorHandler` global qui ignore toutes les erreurs et remplace celui d'Avalonia pour tout le processus (`X11Native.cs:22,33,41-44`). |
| M32 | Sentry : aucun nettoyage des données personnelles dans les messages d'exception (chemins contenant le nom d'utilisateur, `Failed to start process '<chemin>'`). `config_load_failure` (erreur attendue) est envoyé comme exception. Pas de `SampleRate`. Les builds debug envoient au même projet. |
| M33 | Appel OS direct depuis l'UI (`MainWindow.axaml.cs:233-238`, `Process` non libéré). `User32Functions` est `public` et expose du P/Invoke à l'assembly UI. |
| M34 | Dépendances statiques non mockables : `ConfigFileAccessor.GetInstance()`, `StaticData.AppClosing` mutable, `AppServiceProvider` utilisé comme localisateur de services. Les tests instancient `ConfigFileAccessor` par réflexion. |
| M35 | API publiques de `WindowSwitcher.Lib` sans documentation XML : `ConfigFileAccessor`, `ConfigFile`, `WindowConfig`, `StaticData`. |

---

## 6. Constats faibles

- **Allocations par frame** : environ 1 Ko géré (lease, délégués, closures, `DispatcherOperation`, `FirstOrDefault` sur `List` dans le stock de tampons). Environ 0,6 Mo/s en Gen0 pour 10 × 60 FPS, pas de LOH. Négligeable. [V]
- **Clavier, chemin chaud Linux** : par événement natif, un CTS lié avec un timer `CancelAfter`, un record, une recherche dans un `Dictionary<string>`. Par `EV_KEY`, la chaîne `KEY_{code}`, `ToUpperInvariant`, `Enum.TryParse`, 2 verrous et 2 `GetInvocationList`. Aucun log. Recommandation : table précalculée code → touche, `TryWrite` avant `WriteAsync`. [V]
- **Snapshot du catalogue** reconstruit sous le verrou attendu par le hook (runtime l.233-236). Le construire hors verrou et le publier avec `Volatile.Write`. [V]
- **`SYN_DROPPED`** n'est pas traité (`LinuxGlobalKeyboardListener.cs:706-713`). [V]
- **`NativeFrameBufferPool.cs:33-34`** : `FreeHGlobal` avant `AllocHGlobal`. Si l'allocation lève une OOM, `entry.Pointer` reste pendant. [V]
- **Lecture hors limites possible** : si `crop_offset >= available`, `available` n'est pas réduit (`c:444-449`). [V]
- **`DmaBufPreviewControl.cs:140-149`** : un rendu sans nouvelle frame (redimensionnement, exposition) efface l'aperçu, qui reste vide si la source est statique. [H]
- **Cadence X11** : `lastFrameTimestamp` est pris après la capture et le `yield`, ce qui donne environ 15-18 FPS au lieu de 20 (`X11PreviewFrameProvider.cs:84`). [V]
- **Aperçus désactivés** : chaque fenêtre garde une boucle à 100 ms (`FloatingWindowService.cs:405`). [V]
- **Fuite de session possible** : `GetOrCreateSession` ne vérifie pas le token, et une sortie par annulation ne retire jamais la session. [V course]
- **Position** mise à jour seulement sur `PointerReleased` et sauvegardée seulement à la fermeture, donc perdue en cas de crash. [H]
- **Clic droit** sur un aperçu : il active aussi la fenêtre, et `BeginMoveDrag` est lancé quel que soit le bouton (`FloatingWindow.axaml.cs:254`). [V]
- **Ratio d'aspect** non conservé (`Stretch="Fill"`, `rcDestination` plein). `fSourceClientAreaOnly=false` inclut le cadre de la fenêtre. Opacité 0,8 sous Linux, 1,0 sous Windows. [V]
- **`TempWindowIdsBlacklist`** n'est jamais purgé alors que les XID et les HWND sont réutilisés. [V]
- **Préfixes** : les entrées non normalisées du `config.json` ne peuvent pas être supprimées depuis l'UI, et sélectionner un élément le supprime, donc la navigation au clavier efface des entrées (`PrefixListService.cs:51-67`, `PrefixListViewModel.cs:57-63`). [V]
- **Build Store** : message trompeur « Update check failed: Updates are managed by Microsoft Store. » (`AppInfoViewModel.cs:142-145`). [V]
- **Windows** : pas de filtrage `DWMWA_CLOAKED` ni `WS_EX_TOOLWINDOW`. `TryActivateWindowAsync` renvoie toujours `true`. `SetWindowText` vers un processus figé peut bloquer. [V]
- **KWin** : il ajoute « <2> » aux titres dupliqués via `_NET_WM_VISIBLE_NAME`, qui est lu en priorité. La blacklist exacte et la clé de position deviennent instables en multibox. [V/H]
- **Renommage** : si le nouveau titre ne contient plus aucun préfixe, la fenêtre disparaît et sa position est perdue. [V]
- **`XInitThreads`** est appelé tardivement (`X11Native.cs:32`). Sans effet avec libX11 ≥ 1.8. [H]
- **Écriture non durable** : pas de `Flush(true)` avant le renommage du fichier temporaire (`ConfigFileAccessor.cs:364`). [V]
- **Raccourci sans modificateur** accepté : il peut avaler une frappe normale. [V]
- **Code mort** :
  - `KeybindPressedStateTracker`, `GlobalKeyboardService.KeyEvent`, `MatchedCombination` ;
  - `SystemInfoService` (`bash -lc`) ;
  - `AddToBlacklistCommand`, `AddToTemporaryBlacklistCommand`, `RenameWindowCommand` (non liées en XAML) ;
  - `RefreshScreenshotWhenDeselected` ;
  - le paramètre du constructeur de `X11PreviewFrameProvider` ;
  - `Rect.AsList`, `Scale`, `MakeSmaller`, `DwmQueryThumbnailSourceSize`.

  [V]
- **Documentation** : `AGENTS.md` cite `Window-Switcher.sln` alors que le dépôt utilise `Window-Switcher.slnx`. Le README annonce une cible de 20 FPS « uniforme », ce qui est inexact sous PipeWire (P1).

---

## 7. Points positifs

- **Frames** : une seule frame en attente par aperçu, la plus récente gagne, l'ancienne est libérée. La pression arrière est bornée.
- **Capture CPU** : `WriteableBitmap` A/B réutilisés, recréés seulement si la taille change. Le buffer PipeWire est rendu juste après la copie.
- **X11** : XDamage est réellement exploité (une fenêtre statique ne coûte rien), XShm avec repli sur `XGetImage`. Pour la découverte : connexion persistante, cache des atomes, tailles de propriétés bornées, `XFree` dans des `finally`, cache des noms de processus avec éviction.
- **Windows** : composition DWM sur GPU, sans coût CPU ni boucle.
- **PipeWire** :
  - `find_latest_buffer` garde la dernière frame ;
  - `LatestFrameChannel` de capacité 1 ;
  - libération unique par `Interlocked.Exchange` ;
  - délégués retenus par des champs ;
  - `GCHandle` libéré après `DestroyStream` ;
  - `BgraScalePlan` en cache ;
  - redimensionnement temporisé à 250 ms ;
  - modifiers négociés avec filtrage de `external_only`.
- **Liste principale** : mise à jour incrémentale de l'`ObservableCollection` (pas de `Clear()`), énumération hors du thread UI, pas de chevauchement des passages, `CancellationToken`.
- **Config** : snapshot pris sous verrou et I/O hors verrou, écriture via fichier temporaire puis renommage, écritures périmées sautées grâce au numéro de version, clones en lecture.
- **Clavier** :
  - décision synchrone minimale et activation hors du thread d'écoute ;
  - catalogue immuable reconstruit seulement sur `BindingsChanged` ;
  - hook sur un thread dédié ;
  - le noyau libère le grab en cas de crash ;
  - clavier virtuel du projet exclu par son identité ;
  - uinput en `O_NONBLOCK`.

  Le correctif e7400b0 est lisible et retire 525 lignes.
- **Mise à jour** : `HttpClient` réutilisé, cache de 5 minutes.
- **Sentry** : capture automatique désactivée, `SendDefaultPii=false`, filtre des exceptions sans intérêt, limitation des diagnostics répétés.

---

## 8. Couverture fonctionnelle par plateforme

| Fonction | Windows | Linux X11 | Linux Wayland (XWayland) | Tests |
|---|---|---|---|---|
| Liste auto-rafraîchie | `EnumWindows` | EWMH (gestionnaire de fenêtres conforme requis, sinon liste vide) | Fenêtres XWayland seulement, fenêtres Wayland natives invisibles | 1 test du ViewModel (filtrage initial). Rien sur les retraits, la sélection ou l'échec |
| Aperçu live | DWM sur GPU, sans limite de FPS | XComposite + XDamage + XShm (C2, P4, M3) | PipeWire via le portail (C1, P1, P2, P3) | Conversion, copie et fabrique. Ni la session, ni la boucle, ni `FloatingWindowService` |
| Clic pour focus | `SetForegroundWindow` + `AttachThreadInput` | `_NET_ACTIVE_WINDOW` | XWayland, selon le compositeur [H] | Transmission (coordinateur) |
| Focus au survol | Oui | Oui | Oui, sous réserve de la prévention du vol de focus | Non |
| Surbrillance de la fenêtre active | Bordure de 2 px autour de la miniature | Oui | Oui, plus une bordure orange pendant la sélection | 4 tests (coordinateur). Ne suit pas le focus de l'OS (M20) |
| Déplacer / redimensionner | `BeginMoveDrag`, poignées de 8 px. DWM réenregistré à chaque événement | Flux redémarré après 250 ms | Idem | Non |
| Taille fixe | Bug M18 | Bug M18 | Bug M18 | Non |
| Persistance position / taille | À la fermeture, clé `process\|titre` | Idem | Idem | Accesseur (Save / Get) |
| Renommage | `SetWindowText` | `_NET_WM_NAME` | XWayland seulement | `RenameViewModel` seulement |
| Filtres préfixe / blacklist | Oui (M16) | Oui | Oui | `PrefixListViewModel` avec un faux service. Ni le service réel, ni le coordinateur de blacklist |
| Blacklist temporaire | En mémoire | Idem | Idem | Non |
| Désactiver les aperçus | Désenregistrement DWM | Session détruite + boucle à 100 ms | Flux suspendu | Non |
| Raccourci vers un client précis | Oui (B3 en AZERTY/QWERTZ) | Oui (P5) | Oui | Activateur avec un accesseur factice |
| Client suivant / précédent | Oui | Oui | Oui | Cycle, ordre stable, sélection. Pas de concurrence |
| Focus du client actif | Dernier client activé par l'application | Idem | Idem | Oui |
| Doublons / conflits de raccourcis | Identité exacte seulement (ni raccourcis OS, ni AltGr) | Idem | Idem | `WindowKeybindManagerTests` |
| Cibles sauvegardées hors ligne | Oui | Oui | Oui | Catalogue oui. Activation hors ligne : renvoie `false` sans rien signaler |
| Capture d'un raccourci dans l'UI | Cassée en AZERTY/QWERTZ (B3) | Oui | Oui | ViewModel (cas de succès). Mapper non testé |
| Listener clavier | Hook bas niveau | evdev + uinput | Idem | Windows 0 %, Linux 68 % |
| Bannière de permissions | Sans objet | Au démarrage seulement (M26) | Idem | ViewModel et service de statut |
| Open data folder | Oui | `xdg-open` requis, sinon plantage (B6) | Idem | Non |
| Clean / Generate config | Incohérent (M19) | Idem | Idem | Accesseur testé, pas la cohérence de l'état |
| Reset all previews | Menu masqué | Menu masqué | PipeWire (C1) | `ResetSelection` testé, pas `ResetAllSelections` |
| Démarrage minimisé / tray | Oui | Selon la zone de notification | Idem | Non |
| About / diagnostics | Oui | Oui | Oui | `AppInfoViewModel` non testé |
| Mise à jour | GitHub `.exe`. Store : message trompeur | AppImage x86_64 | Idem | `GitHubAppUpdateService` (6 tests), service Store (2 tests) |
| macOS | Non implémenté | — | — | — |

---

## 9. Couverture des tests

### 9.1 Par zone (lignes couvertes, exécution Linux)

| Zone | Couverture | Lignes |
|---|---|---|
| UI / racine (`App`, `Program`) | 0,0 % | 0 / 222 |
| UI / Controls (`DmaBufPreviewControl`) | 0,0 % | 0 / 213 |
| UI / Theming | 0,0 % | 0 / 44 |
| Lib / Interop | 0,0 % | 0 / 17 |
| Lib / Graphics | 0,0 % | 0 / 5 |
| UI / Windows (fenêtres + services) | 5,1 % | 102 / 1 988 |
| Lib / Policies | 18,2 % | 10 / 55 |
| Lib / PreviewFrames | 38,5 % | 1 039 / 2 698 |
| Lib / Keybinds / Listeners | 41,7 % | 551 / 1 321 |
| Lib / WindowAccess / Accessors | 47,6 % | 295 / 620 |
| Lib / Diagnostics | 49,6 % | 64 / 129 |
| UI / ViewModels | 55,1 % | 419 / 761 |
| Lib / Data / Updates | 67,6 % | 165 / 244 |
| Lib / Keybinds / Runtime | 68,2 % | 148 / 217 |
| UI / Hosting | 68,5 % | 76 / 111 |
| Lib / Keybinds / Utilities | 79,1 % | 356 / 450 |
| UI / Diagnostics (Sentry) | 81,2 % | 311 / 383 |
| Lib / Keybinds / Services | 82,7 % | 610 / 738 |
| Lib / Data (`ConfigFileAccessor`) | 88,8 % | 324 / 365 |
| Lib / Keybinds / Models | 92,8 % | 141 / 152 |
| Lib / WindowAccess / Factories | 93,3 % | 98 / 105 |
| Lib / SystemInfo | 97,0 % | 32 / 33 |
| Lib / Models | 100,0 % | 111 / 111 |

### 9.2 Fichiers importants à 0 % ou presque

| Fichier | Couverture | Lignes |
|---|---|---|
| `Windows/Services/FloatingWindowService.cs` | 0 % | 451 |
| `PreviewFrames/X11/X11WindowCaptureSession.cs` | 0 % | 433 |
| `Windows/FloatingWindow.axaml.cs` | 0 % | 306 |
| `Windows/MainWindow.axaml.cs` | 0 % | 252 |
| `Controls/DmaBufPreviewControl.cs` | 0 % | 208 |
| `Keybinds/Listeners/Windows/WindowsGlobalKeyboardListener.cs` | 0 % | 206 |
| `PreviewFrames/Pipewire/LinuxDmaBufEglInterop.cs` | 0 % | 197 |
| `WindowAccess/Accessors/WindowsWinAccessor.cs` | 0 % | 170 |
| `App.axaml.cs` | 0 % | 161 |
| `ViewModels/AppInfoViewModel.cs` | 0 % | 136 |
| `Windows/Services/FloatingWindowRegistry.cs` | 0 % | 81 |
| `Windows/Keybinds/AvaloniaKeybindCaptureMapper.cs` | 0 % | 71 |
| `Windows/Services/PrefixListService.cs` | 0 % | 47 |
| `Keybinds/Listeners/Linux/InputEventsCore/Discovery/InputDeviceDiscovery.cs` | 0,7 % | 142 |
| `PreviewFrames/Pipewire/PipeWireFrameProvider.Stream.cs` | 6,2 % | 353 |
| `Keybinds/Listeners/Linux/LinuxUinputKeyboardForwarder.cs` | 7,6 % | 119 |
| `PreviewFrames/X11/X11PreviewFrameProvider.cs` | 17,8 % | 129 |
| `PreviewFrames/Pipewire/PipeWireFrameProvider.cs` | 39,8 % | 846 |
| `Native/window_switcher_pipewire.c` | non mesuré | — |

### 9.3 Faux positifs et absence de CI

- **14 tests** se terminent par un simple `if (!OperatingSystem.IsXxx()) return;`. Ils sont comptés comme réussis sans rien vérifier sur l'autre plateforme :
  - `PreviewFrameProviderFactoryTests` : 6 tests ;
  - `PipeWireFrameProviderTests` : 6 tests ;
  - `X11EwmhWindowAccessorTests` : 1 test ;
  - `WindowsWinAccessorTests` : 1 test.
- D'autres tests ont des branches selon l'OS : `RuntimeWinAccessorFactoryTests`, `RuntimeGlobalKeyboardListenerFactoryTests`, `GitHubAppUpdateServiceTests`, `PlatformCommandRunnerIntegrationTests`, `PlatformServiceCollectionExtensionsTests`, `ProcessExecutionTests`.
- Il n'existe **aucune CI**. Le code Windows n'est donc exécuté par aucun test, et la règle « build et tests verts avant soumission » d'`AGENTS.md` n'est vérifiée nulle part.
- Les builds avec `EnableSentryTelemetry=false` retirent `AppExceptionTests.cs` (19 tests), et aucune configuration ne les vérifie.

### 9.4 Freins à la testabilité

- `ConfigFileAccessor.GetInstance()` (singleton statique), instancié par réflexion dans les tests.
- `AppServiceProvider` utilisé comme localisateur de services dans les constructeurs de `FloatingWindow` et des fenêtres utilitaires.
- `FloatingWindowService` dépend de contrôles Avalonia concrets (`Image`, `DmaBufPreviewControl`) et de `Dispatcher.UIThread`. La logique de cycle de vie (regroupement des frames, annulation, temporisation, suspension) n'est donc pas testable sans runtime UI, contrairement à ce qu'exige `AGENTS.md`.
- `LinuxNative` (evdev, uinput, ioctl) n'est pas abstrait, donc la boucle evdev n'est pas testable.
- Le code C n'a pas de banc de test.

### 9.5 Scénarios importants non testés

- **Aperçus** :
  - plusieurs sessions X11 qui se partagent le stock de tampons (C2) ;
  - destruction d'un flux avec des leases en cours (C1) ;
  - `SetActive` / `Drain`, `DisableDmaBuf`, `UpdateTargetDimensions` ;
  - redémarrage via `InvalidateCapture` ;
  - `ResetAllSelections`.
- **Cadence** : 20 FPS effectifs par provider.
- **Liste des fenêtres** : échec passager de l'énumération (B1), retraits, sélection.
- **Config** : `IOException` à l'écriture, mises à jour concurrentes, sauvegarde d'un fichier corrompu, cohérence après `Reset` / `Clean`, préfixes non normalisés.
- **Clavier** :
  - runtime alimenté par des événements Linux ;
  - modificateurs gauche/droite ;
  - relâchements perdus ;
  - touches tenues pendant une régénération ;
  - appuis rapides concurrents ;
  - correspondance capture ↔ exécution par disposition de clavier ;
  - `AvaloniaKeybindCaptureMapper`.
- **Mise à jour** : timeout, coupure pendant la lecture du corps de la réponse.

---

## 10. Plan d'action

### Lot 1 — Stabilité (priorité immédiate)

1. **C1** : supprimer l'attente synchrone des leases à la destruction d'un flux PipeWire.
2. **C2** : un stock de tampons par session X11, et distinguer « stock vide » de « capture impossible ».
3. **B1** : distinguer échec et liste vide dans l'énumération des fenêtres.
4. **B2** : conserver une copie de la config corrompue au lieu de l'écraser.

### Lot 2 — Performance des aperçus Linux

1. **P1** : `maxFramerate` et limitation dans `on_process`.
2. **P2** : mise à l'échelle en virgule fixe et SIMD, ou en C.
3. **P3** : fence GL au lieu de `glFinish`, cache d'EGLImage.
4. **M1** : n'afficher le contrôle GL que s'il est utilisé, et `ClearFrame` uniquement lors d'une transition.
5. **P4** : réduction côté serveur X (XRender), géométrie en cache.
6. **M2** : suspendre la capture des aperçus masqués ou minimisés.
7. **M3 / M4** : renommer le pixmap sur `MapNotify` ; une connexion d'événements unique.

### Lot 3 — Clavier

1. **P5** : thread dédié avec `epoll`.
2. **B3** : résolution Windows depuis le scancode, avec tests de correspondance.
3. **B4** : modificateurs relus via `GetAsyncKeyState`, touche masque pour Win/Alt, Reset au changement de session.
4. **P7** : file d'activation et cache de fenêtres.
5. **B5** : watchdog de grab.

### Lot 4 — Configuration et UI

1. **P6** : `UpdateConfigAsync`, regroupement des écritures, aucune écriture sans changement.
2. **M16, M17, M18, M19** : bug de blacklist, titre de l'aperçu, taille fixe, cohérence après `Reset` / `Clean`.
3. **B6** : gérer les exceptions attendues des commandes, `Flush` Sentry.

### Lot 5 — Qualité et outillage

1. CI GitHub Actions Linux + Windows (build, tests, `csharpier check`, build sans Sentry).
2. Remplacer les `return` conditionnels par de vrais tests ignorés (xUnit v3 `Assert.SkipUnless`, ou `Xunit.SkippableFact`).
3. Extraire la logique de `FloatingWindowService` derrière des abstractions testables, et injecter `ConfigFileAccessor`.
4. Abstraire `LinuxNative` pour tester la boucle evdev.
5. Benchmarks BenchmarkDotNet pour `BgraFrameCopier` et `X11FrameConverter`, et un test de cadence par provider.
6. Remplacer les `catch { }` silencieux par une journalisation réelle, et brancher `TracePlatformDiagnostics` sur une sortie effective.
