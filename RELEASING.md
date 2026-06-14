# Publier une nouvelle version

Les versions de MundoBrowser sont construites et publiées automatiquement par le
workflow GitHub Actions `.github/workflows/release.yml`.

## Procédure

1. Choisir le prochain numéro de version au format `MAJEURE.MINEURE.CORRECTIF`.
   Par exemple : `1.1.1`.

2. Modifier la version dans `MundoBrowser/MundoBrowser.csproj` :

   ```xml
   <Version>1.1.1</Version>
   ```

3. Vérifier que le projet compile :

   ```powershell
   dotnet build MundoBrowser\MundoBrowser.csproj -c Release
   ```

4. Enregistrer et pousser les changements :

   ```powershell
   git add .
   git commit -m "Prépare la version 1.1.1"
   git push origin master
   ```

5. Créer et pousser le tag correspondant. Le préfixe `v` est obligatoire :

   ```powershell
   git tag v1.1.1
   git push origin v1.1.1
   ```

6. Vérifier le workflow `Release` dans l'onglet **Actions** de GitHub, puis
   vérifier que la nouvelle version apparaît dans **Releases**.

Les utilisateurs ayant installé MundoBrowser avec l'installeur Velopack recevront
la mise à jour automatiquement au prochain lancement.

## En cas d'erreur

- La version du tag doit correspondre à celle du fichier `.csproj`.
- Une version déjà publiée ne doit pas être réutilisée.
- Ne pas utiliser Inno Setup : l'installeur et les packages de mise à jour sont
  générés par Velopack.
