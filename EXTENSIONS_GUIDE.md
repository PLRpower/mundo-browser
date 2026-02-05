# Installation d'Extensions depuis le Chrome Web Store

## Fonctionnalité

MundoBrowser vous permet maintenant d'installer des extensions directement depuis le Chrome Web Store sans avoir à les télécharger et les décompresser manuellement.

## Comment utiliser

### Étape 1: Ouvrir le gestionnaire d'extensions
1. Cliquez sur le bouton **"Load Extension"** dans la barre latérale du navigateur

### Étape 2: Ajouter une extension
Vous avez deux options pour ajouter une extension:

#### Option A: Utiliser l'URL du Chrome Web Store
1. Allez sur [Chrome Web Store](https://chrome.google.com/webstore)
2. Trouvez l'extension que vous voulez installer
3. Copiez l'URL de la page de l'extension (par exemple: `https://chrome.google.com/webstore/detail/bitwarden/nngceckbapebfimnlniiiahkandclblb`)
4. Collez l'URL dans la fenêtre d'installation
5. Cliquez sur **"Install"**

#### Option B: Utiliser l'ID de l'extension
1. L'ID de l'extension est la partie à 32 caractères à la fin de l'URL
2. Par exemple, pour Bitwarden: `nngceckbapebfimnlniiiahkandclblb`
3. Entrez simplement l'ID dans la fenêtre d'installation
4. Cliquez sur **"Install"**

## Extensions Populaires à Essayer

### Bitwarden (Gestionnaire de mots de passe)
- **URL**: https://chrome.google.com/webstore/detail/bitwarden-free-password-m/nngceckbapebfimnlniiiahkandclblb
- **ID**: `nngceckbapebfimnlniiiahkandclblb`

### uBlock Origin (Bloqueur de publicités)
- **URL**: https://chrome.google.com/webstore/detail/ublock-origin/cjpalhdlnbpafiamejdnhcphjbkeiagm
- **ID**: `cjpalhdlnbpafiamejdnhcphjbkeiagm`

### Dark Reader (Mode sombre)
- **URL**: https://chrome.google.com/webstore/detail/dark-reader/eimadpbcbfnmbkopoojfekhnkhdbieeh
- **ID**: `eimadpbcbfnmbkopoojfekhnkhdbieeh`

### LastPass (Gestionnaire de mots de passe)
- **URL**: https://chrome.google.com/webstore/detail/lastpass-free-password-ma/hdokiejnpimakedhajhdlcegeplioahd
- **ID**: `hdokiejnpimakedhajhdlcegeplioahd`

### Grammarly (Vérificateur de grammaire)
- **URL**: https://chrome.google.com/webstore/detail/grammarly-grammar-checker/kbfnbcaeplbcioakkpcpgfkobkghlhen
- **ID**: `kbfnbcaeplbcioakkpcpgfkobkghlhen`

## Processus Technique

Quand vous installez une extension:
1. L'extension est téléchargée depuis le Chrome Web Store
2. Le fichier CRX (Chrome Extension) est automatiquement extrait
3. L'extension est installée dans le profil WebView2 de MundoBrowser
4. L'extension devient immédiatement disponible dans votre navigateur

## Stockage des Extensions

Les extensions téléchargées sont stockées dans:
```
C:\Users\[VotreNom]\AppData\Local\MundoBrowser\Extensions\
```

Chaque extension a son propre dossier nommé selon son ID.

## Dépannage

### L'installation échoue
- Vérifiez que vous avez une connexion Internet active
- Assurez-vous que l'ID ou l'URL est correct
- Certaines extensions peuvent ne pas être compatibles avec WebView2

### L'extension ne s'affiche pas
- Redémarrez MundoBrowser
- Vérifiez que l'extension est compatible avec Chromium

### Erreur de téléchargement
- Vérifiez votre connexion Internet
- Essayez de télécharger à nouveau
- L'extension peut avoir été retirée du Chrome Web Store
