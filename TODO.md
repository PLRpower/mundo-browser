# Mundo Browser - TODO List

- [ ] **Gestionnaire de tâches** : Rassembler la partie WebView2 et Mundo Browser dans la même section pour une meilleure visibilité système.
- [ ] **Affichage (F11)** : Mieux gérer les problèmes d'affichage et de bordures lors du passage en mode plein écran.
- [ ] **Favicons** : Améliorer la détection, le cache et l'affichage des icônes de sites (favicons) dans les onglets.
- [ ] **Split-View (Multitâche natif)** : Permettre de diviser l'écran en deux (ou plus) pour afficher plusieurs onglets simultanément dans la même fenêtre. Implémenter un système de glisser-déposer pour organiser les vues.
- [ ] **Mundo Search Map (IA Game Changer)** : Remplacer la liste de résultats linéaire par une carte mentale (Mind Map) interactive et catégorisée par IA.
    - **Visualisation** : Utiliser un graphe dynamique (D3.js ou Cytoscape) sur une page d'accueil locale (`internals.mundobrowser`).
    - **Classification IA** : Envoyer les métadonnées des résultats de recherche à un LLM pour générer des branches logiques (ex: "Comparatifs", "Prix", "Apprentissage", "Vidéos").
    - **Interaction** :
        - Clic simple : Ouvre le lien dans l'onglet courant.
        - Ctrl+Clic / Glisser-déposer : Ouvre le lien directement en mode **Split-View**.
        - Zoom spatial : Permettre de naviguer dans la carte pour découvrir des sous-sujets.
    - **Dashboard de reprise** : Sauvegarder les cartes de recherche récentes pour permettre à l'utilisateur de reprendre ses explorations là où il les a laissées.

