# Mundo Browser - TODO List

- [x] **Système d'auto update** : Implémenter un mécanisme de mise à jour automatique pour le navigateur, incluant la vérification des nouvelles versions, le téléchargement et l'installation sans intervention manuelle.

  Je recommande de remplacer progressivement Inno Setup par Velopack. C’est un framework .NET conçu pour fournir un installateur, un processus Update.exe, des mises à jour
  automatiques, des packages différentiels et le redémarrage de l’application.

  Développer notre propre updater serait possible, mais il faudrait gérer nous-mêmes les fichiers verrouillés, l’élévation administrateur, les signatures, les
  téléchargements interrompus, les rollbacks et le redémarrage.

  Comportement proposé

  Au lancement :

    1. Un écran MundoBrowser minimal apparaît.
    2. Vérification de mise à jour avec un délai court.
    3. Si aucune mise à jour ou aucune connexion : ouverture normale du navigateur.
    4. Si une mise à jour existe :
        - téléchargement avec progression ;
        - application par le processus updater séparé ;
        - redémarrage automatique de MundoBrowser.

    5. En cas d’échec, lancement de la version actuelle.

  Intégration dans MundoBrowser

  Les modifications principales seraient :

    - Ajouter une version SemVer dans MundoBrowser.csproj :

  <Version>1.1.0</Version>

    - Ajouter le package NuGet Velopack.
    - Exécuter VelopackApp.Build().Run() avant le mutex single-instance actuel.
    - Ajouter un UpdateService chargé de vérifier et télécharger les versions.
    - Ajouter une petite fenêtre de progression de mise à jour.
    - Remplacer la version 1.0.0 codée en dur dans les paramètres par la version réelle de l’assembly.
    - Publier les releases automatiquement avec GitHub Actions.

  Exemple simplifié :

  var manager = new UpdateManager(
  new GithubSource("https://github.com/PLRpower/mundo-browser", null, false));

  var update = await manager.CheckForUpdatesAsync();

  if (update != null)
  {
  await manager.DownloadUpdatesAsync(update);
  manager.ApplyUpdatesAndRestart(update);
  }

  Distribution

  À chaque tag GitHub comme v1.1.0, GitHub Actions pourrait :

    1. Publier MundoBrowser.
    2. Générer les packages Velopack.
    3. Générer l’installateur.
    4. Générer les mises à jour différentielles.
    5. Envoyer les fichiers dans GitHub Releases.

  Je déconseille le fichier unique compressé actuel pour les releases automatiques : un petit changement peut modifier presque tout l’exécutable et rendre les mises à jour
  différentielles volumineuses. Un dossier publié self-contained sera plus efficace.

  Point important

  L’installeur Inno actuel installe dans Program Files, ce qui peut demander les droits administrateur à chaque mise à jour. Pour une expérience proche de Discord,
  MundoBrowser devrait être installé par utilisateur dans %LocalAppData%.

  Les données utilisateur sont déjà stockées hors du dossier applicatif dans %LocalAppData%\MundoBrowser, elles survivront donc correctement aux mises à jour.

  Il faudra également signer numériquement les releases avant une distribution publique. Velopack recommande fortement la signature de code.

  Sources : Velopack C# (https://docs.velopack.io/getting-started/csharp), intégration (https://docs.velopack.io/integrating/overview), installateurs
  (https://docs.velopack.io/packaging/installer), GitHub Actions (https://docs.velopack.io/distributing/github-actions).

- [ ] **Migration de WebView2 vers CefSharp / CEF** : Remplacer le moteur Microsoft WebView2 par Chromium Embedded Framework via CefSharp.
    - **DevTools intégrés** : Afficher et masquer la console développeur dans un panneau ancré à droite du navigateur.
    - **Identité des processus** : Remplacer les processus `msedgewebview2.exe` par des processus CEF appartenant à Mundo Browser afin d'améliorer leur regroupement et leur visibilité dans le Gestionnaire des tâches Windows.
    - **Performances** : Évaluer puis optimiser le rendu, la fluidité, la consommation mémoire et la suspension des onglets avec CEF.
    - **Indépendance** : Ne plus dépendre du runtime Microsoft Edge WebView2 et maîtriser directement la version de Chromium distribuée avec Mundo Browser.
    - **Compatibilité fonctionnelle** : Porter les onglets, sessions, paramètres, extensions, bloqueurs, raccourcis, pages internes, téléchargements et comportements plein écran avant de retirer WebView2.
- [ ] **Split-View (Multitâche natif)** : Permettre de diviser l'écran en deux (ou plus) pour afficher plusieurs onglets simultanément dans la même fenêtre. Implémenter un système de glisser-déposer pour organiser les vues.
- [ ] **Mundo Search Map (IA Game Changer)** : Remplacer la liste de résultats linéaire par une carte mentale (Mind Map) interactive et catégorisée par IA.
    - **Visualisation** : Utiliser un graphe dynamique (D3.js ou Cytoscape) sur une page d'accueil locale (`internals.mundobrowser`).
    - **Classification IA** : Envoyer les métadonnées des résultats de recherche à un LLM pour générer des branches logiques (ex: "Comparatifs", "Prix", "Apprentissage", "Vidéos").
    - **Interaction** :
        - Clic simple : Ouvre le lien dans l'onglet courant.
        - Ctrl+Clic / Glisser-déposer : Ouvre le lien directement en mode **Split-View**.
        - Zoom spatial : Permettre de naviguer dans la carte pour découvrir des sous-sujets.
    - **Dashboard de reprise** : Sauvegarder les cartes de recherche récentes pour permettre à l'utilisateur de reprendre ses explorations là où il les a laissées.

