# Mundo Browser - TODO List

- [ ] **Migration de WebView2 vers CefSharp / CEF** : Remplacer le moteur Microsoft WebView2 par Chromium Embedded Framework via CefSharp.
    - **DevTools intégrés** : Afficher et masquer la console développeur dans un panneau ancré à droite du navigateur.
    - **Identité des processus** : Remplacer les processus `msedgewebview2.exe` par des processus CEF appartenant à Mundo Browser afin d'améliorer leur regroupement et leur visibilité dans le Gestionnaire des tâches Windows.
    - **Performances** : Évaluer puis optimiser le rendu, la fluidité, la consommation mémoire et la suspension des onglets avec CEF.
    - **Indépendance** : Ne plus dépendre du runtime Microsoft Edge WebView2 et maîtriser directement la version de Chromium distribuée avec Mundo Browser.
    - **Compatibilité fonctionnelle** : Porter les onglets, sessions, paramètres, extensions, bloqueurs, raccourcis, pages internes, téléchargements et comportements plein écran avant de retirer WebView2.
