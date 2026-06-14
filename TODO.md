# Mundo Browser - TODO List

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
