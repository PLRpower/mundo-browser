# Extension Support - MundoBrowser

## 🧩 How to Load Browser Extensions

MundoBrowser supports loading Chrome/Edge extensions programmatically. Since WebView2 doesn't allow direct access to the Chrome Web Store, you'll need to load extensions manually.

## 📋 Steps to Load an Extension

### 1. **Download the Extension**

You have two options:

#### Option A: Download as CRX (Recommended for advanced users)
- Go to Chrome Web Store
- Find your extension
- Use a CRX downloader tool or browser extension to download the `.crx` file
- Extract the CRX file to a folder

#### Option B: Download Unpacked Extension (Easiest)
1. Visit the extension's GitHub repository (if available)
2. Download the source code
3. Look for the extension folder (usually contains `manifest.json`)

**Example popular extensions available on GitHub:**
- uBlock Origin: https://github.com/gorhill/uBlock
- Dark Reader: https://github.com/darkreader/darkreader
- Bitwarden: https://github.com/bitwarden/clients

### 2. **Prepare the Extension Folder**

- Make sure the folder contains a `manifest.json` file
- The folder structure should look like:
  ```
  ExtensionFolder/
  ├── manifest.json
  ├── background.js
  ├── content.js
  ├── icons/
  └── ...other files
  ```

### 3. **Load in MundoBrowser**

1. Open MundoBrowser
2. Look at the left sidebar
3. Click the **"🧩 Load Extension"** button (green button at the bottom)
4. Select the **folder** containing the unpacked extension
5. If successful, you'll see a confirmation message with the extension name

## ⚙️ Extension Management

Currently supported:
- ✅ Load unpacked extensions
- ✅ Extensions persist across browser sessions (using WebView2 profile)
- ✅ Extensions work with full Chrome API support

## 🔍 Troubleshooting

### "Failed to load extension" error
- **Check manifest.json**: Make sure the file exists and is valid JSON
- **Check manifest version**: WebView2 supports Manifest V2 and V3
- **Check file permissions**: Make sure the folder is readable

### Extension doesn't appear
- Some extensions require specific permissions
- Check the Debug output window for error messages
- Try a different, simpler extension first (like Dark Reader)

## 📦 Recommended Extensions to Try

1. **Dark Reader** - Dark mode for websites
   - GitHub: https://github.com/darkreader/darkreader
   - Download releases, extract, and load the `src/` folder

2. **uBlock Origin** - Ad blocker
   - GitHub: https://github.com/gorhill/uBlock
   - Download releases, extract, and load the `dist/` folder

## 🚀 Future Enhancements

- Extension marketplace within MundoBrowser
- Automatic extension updates
- Extension settings UI
- Disable/Enable extensions without removing
