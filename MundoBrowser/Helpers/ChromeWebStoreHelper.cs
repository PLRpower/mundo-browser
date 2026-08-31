using System.Text.RegularExpressions;

namespace MundoBrowser.Helpers;

public static partial class ChromeWebStoreHelper
{
    private static readonly Regex ExtensionIdRegex = new Regex(@"^[a-p]{32}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsChromeWebStoreUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.Contains("chromewebstore.google.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("chrome.google.com/webstore", StringComparison.OrdinalIgnoreCase);
    }

    public static string? ExtractExtensionId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !IsChromeWebStoreUrl(url)) return null;

        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                var segment = segments[i].Trim().ToLowerInvariant();
                if (segment.Length == 32 && ExtensionIdRegex.IsMatch(segment))
                {
                    return segment;
                }
            }
        }
        catch
        {
            // Ignore URI parsing errors
        }

        return null;
    }

    public const string InjectionScript = @"
(function() {
    try {
        const host = window.location.hostname || '';
        if (!host.includes('chromewebstore.google.com') && !host.includes('chrome.google.com')) {
            return;
        }

        if (window.__mundoWebStoreInjected) {
            if (typeof window.__mundoCheckStore === 'function') {
                window.__mundoCheckStore();
            }
            return;
        }
        window.__mundoWebStoreInjected = true;

        function getExtensionId() {
            try {
                const path = window.location.pathname || '';
                const match = path.match(/(?:detail\/(?:[^\/]+\/)?|\/)([a-p]{32})(?:[\/?#]|$)/i);
                if (match && match[1]) {
                    return match[1].toLowerCase();
                }
                const generalMatch = window.location.href.match(/([a-p]{32})/i);
                if (generalMatch && generalMatch[1]) {
                    return generalMatch[1].toLowerCase();
                }
            } catch (e) {}
            return null;
        }

        let currentExtId = null;
        let isCurrentInstalled = false;
        let isInstalling = false;
        let isUpdating = false;
        let updateTimer = null;
        let lastCheckedId = null;

        function scheduleUpdate(delay = 100) {
            if (updateTimer) clearTimeout(updateTimer);
            updateTimer = setTimeout(() => {
                updateTimer = null;
                updateAllStoreButtons();
            }, delay);
        }

        function checkInstalledStatus(extId) {
            if (!extId || extId === lastCheckedId) return;
            lastCheckedId = extId;
            if (window.chrome && window.chrome.webview) {
                window.chrome.webview.postMessage({
                    type: 'checkExtensionStatus',
                    extensionId: extId
                });
            }
        }

        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.addEventListener('message', function(event) {
                try {
                    const data = event.data;
                    if (!data || typeof data !== 'object') return;
                    
                    if (data.type === 'extensionStatus') {
                        if (!data.extensionId || data.extensionId === currentExtId) {
                            isCurrentInstalled = !!data.isInstalled;
                            isInstalling = false;
                            scheduleUpdate(0);
                        }
                    } else if (data.type === 'extensionInstallProgress') {
                        if (!data.extensionId || data.extensionId === currentExtId) {
                            if (data.status === 'downloading' || data.status === 'installing') {
                                isInstalling = true;
                                updateAllStoreButtons(data.status === 'downloading' ? 'Téléchargement...' : 'Installation...');
                            } else if (data.status === 'installed') {
                                isInstalling = false;
                                isCurrentInstalled = true;
                                scheduleUpdate(0);
                            } else if (data.status === 'error') {
                                isInstalling = false;
                                updateAllStoreButtons('Erreur installation', true);
                                setTimeout(() => {
                                    scheduleUpdate(0);
                                }, 3500);
                            }
                        }
                    }
                } catch (err) {
                    console.error('[Mundo] Error handling webview message', err);
                }
            });
        }

        function getCandidateButtons() {
            const buttons = Array.from(document.querySelectorAll('button, [role=""button""], a[role=""button""]'));
            try {
                const customEls = document.querySelectorAll('*');
                for (let i = 0; i < customEls.length; i++) {
                    const sr = customEls[i].shadowRoot;
                    if (sr) {
                        const srButtons = sr.querySelectorAll('button, [role=""button""], a[role=""button""]');
                        for (let j = 0; j < srButtons.length; j++) {
                            buttons.push(srButtons[j]);
                        }
                    }
                }
            } catch (e) {}
            return buttons;
        }

        function isStoreButton(btn) {
            if (!btn || btn.nodeType !== 1) return false;
            if (btn.dataset.mundoManaged === 'true') return true;

            const text = (btn.innerText || btn.textContent || '').trim().toLowerCase();
            const ariaLabel = (btn.getAttribute('aria-label') || '').toLowerCase();
            
            const matchesAddText = 
                /(?:ajouter|installer|add|añadir|adicionar|aggiungi|toevoegen|hinzufügen|obter).*(?:chrome|google chrome|mundo)/i.test(text) ||
                /(?:ajouter|installer|add|añadir|adicionar|aggiungi|toevoegen|hinzufügen|obter).*(?:chrome|google chrome|mundo)/i.test(ariaLabel) ||
                /(?:supprimer|remove|désinstaller).*(?:chrome|mundo)/i.test(text) ||
                /(?:supprimer|remove|désinstaller).*(?:chrome|mundo)/i.test(ariaLabel) ||
                (text.includes('chrome') && (text.includes('ajouter') || text.includes('add') || text.includes('installer') || text.includes('ajout'))) ||
                (ariaLabel.includes('chrome') && (ariaLabel.includes('ajouter') || ariaLabel.includes('add') || ariaLabel.includes('installer')));

            const matchesIncompatible = 
                /(?:non compatible|incompatible|disponible uniquement|available only|not compatible)/i.test(text) ||
                /(?:non compatible|incompatible|disponible uniquement|available only|not compatible)/i.test(ariaLabel);

            return matchesAddText || matchesIncompatible;
        }

        function transformButton(btn, customText, isError) {
            if (!btn) return;

            const extId = getExtensionId();
            if (!extId) return;

            let targetText = 'Ajouter à Mundo Browser';
            let statusKey = 'ready';
            if (customText) {
                targetText = customText;
                statusKey = 'custom';
            } else if (isCurrentInstalled) {
                targetText = '✓ Ajoutée à Mundo Browser';
                statusKey = 'installed';
            } else if (isInstalling) {
                targetText = 'Téléchargement...';
                statusKey = 'installing';
            }

            if (btn.dataset.mundoStatus === statusKey && btn.dataset.mundoText === targetText && btn._mundoHandled) {
                return;
            }

            btn.dataset.mundoManaged = 'true';
            btn.dataset.mundoStatus = statusKey;
            btn.dataset.mundoText = targetText;

            if (btn.hasAttribute('disabled')) btn.removeAttribute('disabled');
            if (btn.disabled) btn.disabled = false;
            if (btn.getAttribute('aria-disabled') === 'true') {
                btn.setAttribute('aria-disabled', isCurrentInstalled ? 'true' : 'false');
            }
            btn.style.pointerEvents = 'auto';
            btn.style.cursor = isCurrentInstalled ? 'default' : 'pointer';

            if (isCurrentInstalled) {
                btn.style.backgroundColor = '#1e7e34';
                btn.style.borderColor = '#1e7e34';
                btn.style.color = '#ffffff';
            } else if (isError) {
                btn.style.backgroundColor = '#d32f2f';
                btn.style.borderColor = '#d32f2f';
                btn.style.color = '#ffffff';
            }

            let replaced = false;
            function updateTextInNode(node) {
                if (node.nodeType === 3) {
                    const val = node.nodeValue.trim();
                    if (val.length > 0 && (
                        /chrome/i.test(val) || 
                        /ajouter/i.test(val) || 
                        /installer/i.test(val) || 
                        /add/i.test(val) || 
                        /mundo/i.test(val) || 
                        /compatible/i.test(val) || 
                        /téléchargement/i.test(val) || 
                        /installation/i.test(val) ||
                        /erreur/i.test(val)
                    )) {
                        node.nodeValue = ' ' + targetText + ' ';
                        replaced = true;
                    }
                } else if (node.nodeType === 1 && node.childNodes) {
                    for (let i = 0; i < node.childNodes.length; i++) {
                        updateTextInNode(node.childNodes[i]);
                    }
                }
            }

            updateTextInNode(btn);

            if (!replaced) {
                const spans = btn.querySelectorAll('span, div');
                let lastSpan = null;
                for (let i = 0; i < spans.length; i++) {
                    if (spans[i].children.length === 0 && spans[i].textContent.trim().length > 0) {
                        lastSpan = spans[i];
                    }
                }
                if (lastSpan) {
                    lastSpan.textContent = targetText;
                } else {
                    btn.textContent = targetText;
                }
            }

            btn.setAttribute('aria-label', targetText);
            btn.setAttribute('title', targetText);

            if (!btn._mundoHandled) {
                btn._mundoHandled = true;
                btn.addEventListener('click', function(e) {
                    e.preventDefault();
                    e.stopPropagation();
                    e.stopImmediatePropagation();

                    const id = getExtensionId();
                    if (!id) return;

                    if (isCurrentInstalled || isInstalling) {
                        return;
                    }

                    isInstalling = true;
                    updateAllStoreButtons('Téléchargement...');

                    if (window.chrome && window.chrome.webview) {
                        window.chrome.webview.postMessage({
                            type: 'installExtension',
                            extensionId: id
                        });
                    }
                }, true);
            }
        }

        function updateAllStoreButtons(customText, isError) {
            if (isUpdating) return;
            isUpdating = true;
            try {
                const extId = getExtensionId();
                if (!extId) return;

                const buttons = getCandidateButtons();
                for (let i = 0; i < buttons.length; i++) {
                    const el = buttons[i];
                    if (isStoreButton(el)) {
                        transformButton(el, customText, isError);
                    }
                }
            } catch (e) {
                console.error('[Mundo] Error updating store buttons', e);
            } finally {
                isUpdating = false;
            }
        }

        function onUrlChange() {
            try {
                const newExtId = getExtensionId();
                if (newExtId !== currentExtId) {
                    currentExtId = newExtId;
                    lastCheckedId = null;
                    isCurrentInstalled = false;
                    isInstalling = false;
                    if (currentExtId) {
                        checkInstalledStatus(currentExtId);
                    }
                }
                scheduleUpdate(50);
            } catch (e) {
                console.error('[Mundo] onUrlChange error', e);
            }
        }

        window.__mundoCheckStore = function() {
            onUrlChange();
        };

        const originalPushState = history.pushState;
        if (originalPushState) {
            history.pushState = function() {
                const result = originalPushState.apply(this, arguments);
                setTimeout(onUrlChange, 50);
                return result;
            };
        }

        const originalReplaceState = history.replaceState;
        if (originalReplaceState) {
            history.replaceState = function() {
                const result = originalReplaceState.apply(this, arguments);
                setTimeout(onUrlChange, 50);
                return result;
            };
        }

        window.addEventListener('popstate', () => setTimeout(onUrlChange, 50));
        window.addEventListener('hashchange', () => setTimeout(onUrlChange, 50));

        // MutationObserver only watches childList additions, NOT attributes or characterData to avoid any feedback loops
        const observer = new MutationObserver(() => {
            scheduleUpdate(150);
        });

        function startObserving() {
            if (document.body) {
                observer.observe(document.body, {
                    childList: true,
                    subtree: true
                });
            }
        }

        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', () => {
                startObserving();
                onUrlChange();
            });
        } else {
            startObserving();
            onUrlChange();
        }

        // Periodic light sweep (every 3s) as safety
        setInterval(() => {
            const extId = getExtensionId();
            if (extId) {
                if (extId !== currentExtId) {
                    onUrlChange();
                } else if (!isInstalling) {
                    scheduleUpdate(0);
                }
            }
        }, 3000);

        onUrlChange();
    } catch (globalErr) {
        console.error('[Mundo] ChromeWebStore injection error', globalErr);
    }
})();
";
}
