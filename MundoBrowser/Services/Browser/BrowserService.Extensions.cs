using System.IO;
using System.Text.Json;
using MundoBrowser.Services.Extensions;

namespace MundoBrowser.Services.Browser;

public partial class BrowserService
{
    private static string BuildExtensionStoreIntegrationScript(string? pageUrl)
    {
        string? extensionId = ExtensionDownloader.ExtractExtensionIdFromUrl(pageUrl ?? "");
        if (extensionId == null)
            return "";

        string serializedId = JsonSerializer.Serialize(extensionId);
        string serializedInstalledIds = JsonSerializer.Serialize(
            ExtensionRuntime.GetInstalledDirectories().Select(Path.GetFileName));

        return $$"""
            (() => {
                const initialExtensionId = {{serializedId}};
                const installedIds = new Set({{serializedInstalledIds}});
                const labels = {
                    idle: 'Ajouter à MundoBrowser',
                    installing: 'Installation...',
                    installed: 'Ajoutée à MundoBrowser',
                    error: 'Réessayer avec MundoBrowser'
                };
                const targetLabels = [
                    'ajouter a google chrome',
                    'ajouter a chrome',
                    'ajouter au bureau',
                    'add to chrome',
                    'add to desktop',
                    'available on chrome',
                    'supprimer de chrome',
                    'remove from chrome'
                ];
                const getExtensionId = () => location.pathname
                    .split('/')
                    .find(segment => /^[a-p]{32}$/.test(segment)) || initialExtensionId;
                let extensionId = getExtensionId();
                let state = installedIds.has(extensionId) ? 'installed' : 'idle';

                const normalize = value => (value || '')
                    .normalize('NFD')
                    .replace(/[\u0300-\u036f]/g, '')
                    .replace(/\s+/g, ' ')
                    .trim()
                    .toLowerCase();

                const isStoreActionLabel = value => {
                    const normalized = normalize(value);
                    return targetLabels.some(label => normalized === label || normalized.includes(label));
                };

                const findActionElements = () => {
                    const actions = new Set();
                    document.querySelectorAll(
                        '[jsname="wQO0od"], [aria-label], [jsname="V67aGc"], button, [role="button"]')
                        .forEach(element => {
                            const label = element.getAttribute?.('aria-label') || element.textContent;
                            if (element.getAttribute?.('jsname') !== 'wQO0od'
                                && !isStoreActionLabel(label)
                                && element.dataset?.mundoExtensionAction !== 'true')
                                return;

                            const action = element.closest(
                                'button, [role="button"], a, [jsaction*="click"]') || element;
                            actions.add(action);
                        });
                    return actions;
                };

                const updateAction = action => {
                    const text = labels[state];
                    action.dataset.mundoExtensionAction = 'true';
                    if (action.getAttribute('aria-label') !== text)
                        action.setAttribute('aria-label', text);
                    action.style.cursor = state === 'idle' || state === 'error' ? 'pointer' : 'default';

                    action.querySelectorAll('[jsname="V67aGc"], [aria-hidden="true"]')
                        .forEach(label => {
                            if (isStoreActionLabel(label.textContent)
                                || label.dataset.mundoExtensionLabel === 'true') {
                                label.dataset.mundoExtensionLabel = 'true';
                                if (label.textContent !== text)
                                    label.textContent = text;
                            }
                        });

                    if (action.children.length === 0 && action.textContent !== text)
                        action.textContent = text;

                    const disabled = state === 'installing' || state === 'installed';
                    if ('disabled' in action && action.disabled !== disabled)
                        action.disabled = disabled;
                    const ariaDisabled = disabled ? 'true' : 'false';
                    if (action.getAttribute('aria-disabled') !== ariaDisabled)
                        action.setAttribute('aria-disabled', ariaDisabled);
                };

                const updateActions = () => {
                    const currentExtensionId = getExtensionId();
                    if (currentExtensionId !== extensionId) {
                        extensionId = currentExtensionId;
                        state = installedIds.has(extensionId) ? 'installed' : 'idle';
                    }
                    findActionElements().forEach(updateAction);
                };
                let updateScheduled = false;
                const scheduleUpdate = () => {
                    if (updateScheduled)
                        return;

                    updateScheduled = true;
                    requestAnimationFrame(() => {
                        updateScheduled = false;
                        updateActions();
                    });
                };

                window.__mundoExtensionStore = {
                    setState(nextState) {
                        state = nextState;
                        if (nextState === 'installed')
                            installedIds.add(extensionId);
                        updateActions();
                    }
                };

                if (!window.__mundoExtensionStoreClickInstalled) {
                    window.__mundoExtensionStoreClickInstalled = true;
                    document.addEventListener('click', event => {
                        const action = event.target.closest?.('[data-mundo-extension-action="true"]');
                        if (!action || (state !== 'idle' && state !== 'error'))
                            return;

                        event.preventDefault();
                        event.stopPropagation();
                        event.stopImmediatePropagation();
                        state = 'installing';
                        updateActions();
                        window.CefSharp?.PostMessage({
                            type: 'extensionInstallRequested',
                            extensionId
                        });
                    }, true);
                }

                new MutationObserver(scheduleUpdate).observe(document.documentElement, {
                    childList: true,
                    subtree: true,
                    attributes: true,
                    attributeFilter: ['aria-label', 'aria-disabled', 'disabled']
                });
                updateActions();
            })();
            """;
    }
}
