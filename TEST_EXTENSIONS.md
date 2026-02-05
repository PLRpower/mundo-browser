# MundoBrowser - Installation d'Extensions

## 🎉 Nouvelle Fonctionnalité

MundoBrowser peut maintenant installer des extensions **directement depuis le Chrome Web Store** ! Plus besoin de télécharger et décompresser manuellement les extensions.

## 🚀 Comment Tester

### Prérequis
1. Fermez MundoBrowser s'il est en cours d'exécution
2. Compilez le projet: `dotnet build`
3. Lancez l'application: `dotnet run`

### Test de Base - Installer Bitwarden

1. **Lancez MundoBrowser**
   ```powershell
   cd c:\Users\pault\RiderProjects\MundoBrowser\MundoBrowser
   dotnet run
   ```

2. **Cliquez sur "Add Extension"** dans la barre latérale

3. **Entrez l'ID de Bitwarden** dans la fenêtre qui s'ouvre:
   ```
   nngceckbapebfimnlniiiahkandclblb
   ```
   
   OU utilisez l'URL complète:
   ```
   https://chrome.google.com/webstore/detail/bitwarden-free-password-m/nngceckbapebfimnlniiiahkandclblb
   ```

4. **Cliquez sur "Install"**

5. **Attendez** que l'extension soit téléchargée et installée (quelques secondes)

6. **Succès!** Un message de confirmation devrait apparaître

### Autres Extensions à Tester

#### uBlock Origin (Bloqueur de publicités)
```
cjpalhdlnbpafiamejdnhcphjbkeiagm
```

#### Dark Reader (Mode sombre universel)
```
eimadpbcbfnmbkopoojfekhnkhdbieeh
```

#### LastPass
```
hdokiejnpimakedhajhdlcegeplioahd
```

## 📁 Où Sont Stockées les Extensions?

Les extensions téléchargées sont automatiquement sauvegardées dans:
```
C:\Users\pault\AppData\Local\MundoBrowser\Extensions\
```

## ✨ Fonctionnalités Implémentées

- ✅ Téléchargement direct depuis le Chrome Web Store
- ✅ Support des URLs et des IDs d'extensions
- ✅ Extraction automatique des fichiers CRX (format Chrome)
- ✅ Support CRX2 et CRX3
- ✅ Interface utilisateur moderne et intuitive
- ✅ Gestion des erreurs avec messages clairs
- ✅ Barre de progression pendant le téléchargement

## 🔧 Code Modifié

### Nouveaux Fichiers
1. `Services/ExtensionDownloader.cs` - Service de téléchargement et extraction
2. `AddExtensionWindow.xaml` - Interface de la fenêtre d'ajout
3. `AddExtensionWindow.xaml.cs` - Logique de la fenêtre

### Fichiers Modifiés
1. `MainWindow.xaml` - Mise à jour du texte du bouton
2. `MainWindow.xaml.cs` - Utilisation de la nouvelle fenêtre

## 🐛 Dépannage

### Si la compilation échoue avec "fichier en cours d'utilisation"
```powershell
# Fermez MundoBrowser puis:
dotnet build
```

### Si l'extension ne se télécharge pas
- Vérifiez votre connexion Internet
- Assurez-vous que l'ID est correct (32 caractères, lettres a-p uniquement)
- Certaines extensions peuvent avoir des restrictions

### Si l'extension ne s'affiche pas après installation
- Redémarrez MundoBrowser
- Vérifiez que l'extension est compatible avec Chromium/WebView2

## 📝 Notes Techniques

### Format CRX
Les extensions Chrome sont distribuées au format CRX (Chrome Extension), qui est essentiellement un fichier ZIP avec un header spécial. Le service `ExtensionDownloader`:
1. Télécharge le fichier CRX depuis le Chrome Web Store
2. Parse le header CRX (supporte v2 et v3)
3. Extrait le contenu ZIP
4. Installe l'extension dans WebView2

### API Utilisée
```
https://clients2.google.com/service/update2/crx?response=redirect&acceptformat=crx2,crx3&prodversion=119.0.0.0&x=id%3D{extensionId}%26installsource%3Dondemand%26uc
```

Cette API publique de Google permet de télécharger n'importe quelle extension du Chrome Web Store.
