# MundoBrowser - Guide de Déploiement

Ce document explique comment exporter MundoBrowser en un exécutable (.exe) optimisé, léger et autonome pour Windows 11.

## Stratégie de Déploiement

Pour obtenir l'exécutable le plus léger possible tout en garantissant le fonctionnement sur des machines similaires (Windows 11), nous utilisons la publication **Self-Contained** avec **Trim** (élagage du code inutilisé).

### Options de Publication Choisies :
- **Configuration** : Release
- **Runtime cible** : win-x64
- **Mode de déploiement** : Self-Contained (Incorpore le framework .NET pour éviter une installation séparée)
- **Fichier unique** : Oui (`PublishSingleFile=true`)
- **Élagage (Trimming)** : Oui (`PublishTrimmed=true`) pour réduire la taille en supprimant les DLL inutilisées.
- **Compression** : Activée pour le fichier unique.

---

## 1. Exporter l'application via CLI

Pour obtenir l'exécutable le plus léger possible (~80 Mo), lancez cette commande :

```powershell
dotnet publish MundoBrowser\MundoBrowser.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
```

### Pourquoi ces paramètres ?
- `--self-contained false` : Utilise le framework .NET déjà présent sur Windows 11 (gain de ~120 Mo).
- `-p:PublishSingleFile=true` : Un seul .exe contenant tout.
- `-p:EnableCompressionInSingleFile=true` : Compresse le contenu de l'exécutable.
- `-p:DebugType=None` : Supprime les infos de debug pour gagner quelques Mo.

L'exécutable se trouvera dans :
`MundoBrowser\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\`

---

## 2. Gestion du Stockage et des Données

MundoBrowser stocke ses données dans le dossier utilisateur local :
`%LOCALAPPDATA%\MundoBrowser\`

Ce dossier contient :
- `WebView2Data/` : Cache, cookies, historique de navigation (géré par le moteur Edge).
- `history.json` : Historique personnalisé (si implémenté).
- `sessions.json` : Sessions ouvertes.

**Note** : L'exécutable est portable, mais il créera ce dossier sur chaque machine où il est lancé. Pour une portabilité totale "clé USB", il faudrait modifier `WebViewService.cs` pour pointer vers le dossier de l'exécutable.

---

## 3. Dépendance WebView2

L'application utilise **WebView2**. 
- Sur **Windows 11**, le Runtime WebView2 est installé par défaut.
- Si vous ciblez une version de Windows où il n'est pas présent, l'application demandera à l'utilisateur de le télécharger au premier lancement, ou vous pouvez inclure le "Evergreen Bootstrapper" dans un installeur.

---

## 4. Créer un Installeur (Optionnel)

Pour créer un installeur propre (.msi ou setup.exe), nous recommandons **Inno Setup** ou **WiX Toolset**.

Exemple de script simple pour Inno Setup :
1. Pointer vers le fichier `MundoBrowser.exe` généré par `dotnet publish`.
2. Inclure le dossier `Assets/` si des ressources externes ne sont pas incorporées.
3. Créer les raccourcis Bureau et Menu Démarrer.

---

## 5. Optimisations Avancées

Si vous voulez descendre encore plus bas en taille :
- **ReadyToRun** : Améliore le temps de démarrage au détriment de la taille (`-p:PublishReadyToRun=true`).
- **Native AOT** : (Expérimental pour WPF) Permet de compiler directement en code machine. *Non recommandé pour le moment car WPF n'est pas encore totalement compatible AOT sans ajustements complexes.*

---

## 6. Définir comme Navigateur par Défaut

Pour que Windows reconnaisse MundoBrowser comme un navigateur valide, il doit être enregistré dans le registre. Voici un exemple de fichier `.reg` (remplacez `C:\\Chemin\\Vers` par le chemin réel) :

```reg
Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SOFTWARE\Clients\StartMenuInternet\MundoBrowser]
@="MundoBrowser"

[HKEY_LOCAL_MACHINE\SOFTWARE\Clients\StartMenuInternet\MundoBrowser\Capabilities]
"ApplicationDescription"="Navigateur MundoBrowser rapide et léger."
"ApplicationIcon"="C:\\Chemin\\Vers\\MundoBrowser.exe,0"
"ApplicationName"="MundoBrowser"

[HKEY_LOCAL_MACHINE\SOFTWARE\Clients\StartMenuInternet\MundoBrowser\Capabilities\URLAssociations]
"http"="MundoBrowserURL"
"https"="MundoBrowserURL"

[HKEY_LOCAL_MACHINE\SOFTWARE\RegisteredApplications]
"MundoBrowser"="SOFTWARE\\Clients\\StartMenuInternet\\MundoBrowser\\Capabilities"

[HKEY_CLASSES_ROOT\MundoBrowserURL]
@="MundoBrowser Document"
"FriendlyTypeName"="MundoBrowser Document"

[HKEY_CLASSES_ROOT\MundoBrowserURL\shell\open\command]
@="\"C:\\Chemin\\Vers\\MundoBrowser.exe\" \"%1\""
```

Une fois ces clés ajoutées, vous pourrez sélectionner MundoBrowser dans les paramètres Windows. Une option "Définir par défaut" a été ajoutée dans la page `about:preferences` du navigateur pour ouvrir directement la page des réglages Windows.
