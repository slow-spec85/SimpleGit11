using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SimpleGit11.Extensibility.Presentation;
using SimpleGit11.Services;

namespace SimpleGit11.Presentation.Navigation;

internal sealed class PluginMenuHost : IDisposable
{
    private readonly NavigationView _navigation;
    private readonly IAsyncCommandExecutor _executor;
    private readonly IAsyncCommandExceptionHandler _exceptionHandler;
    private readonly Dictionary<NavigationViewItem, PluginMenuItem> _items = [];
    private bool _disposed;

    public PluginMenuHost(
        NavigationView navigation,
        IEnumerable<IMainMenuContribution> contributions,
        IAsyncCommandExecutor executor,
        IAsyncCommandExceptionHandler exceptionHandler)
    {
        _navigation = navigation;
        _executor = executor;
        _exceptionHandler = exceptionHandler;
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        int footerIndex = 0;
        foreach (IMainMenuContribution contribution in contributions)
        {
            PluginMenuItem? model = null;
            try
            {
                model = new PluginMenuItem(contribution, Dispatch);
                if (!ids.Add(model.Id))
                {
                    throw new InvalidOperationException($"Plugin menu id '{model.Id}' is registered more than once.");
                }

                NavigationViewItem item = new() { SelectsOnInvoked = false, Tag = model };
                AutomationProperties.SetAutomationId(item, model.AutomationId);
                Render(item, model.State);
                model.StateChanged += Item_StateChanged;
                if (model.Placement == MainMenuPlacement.Footer)
                {
                    _navigation.FooterMenuItems.Insert(footerIndex++, item);
                }
                else
                {
                    _navigation.MenuItems.Add(item);
                }
                _items.Add(item, model);
            }
            catch (Exception exception)
            {
                model?.Dispose();
                _exceptionHandler.Handle(exception);
            }
        }
    }

    public bool TryInvoke(object? item)
    {
        if (_disposed || item is not NavigationViewItem navigationItem
            || !_items.TryGetValue(navigationItem, out PluginMenuItem? model))
        {
            return false;
        }

        _ = _executor.ExecuteAsync(model.InvokeAsync);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach ((NavigationViewItem item, PluginMenuItem model) in _items)
        {
            model.StateChanged -= Item_StateChanged;
            model.Dispose();
            _navigation.MenuItems.Remove(item);
            _navigation.FooterMenuItems.Remove(item);
        }
        _items.Clear();
    }

    private void Dispatch(Action action)
    {
        if (_disposed)
        {
            return;
        }

        _navigation.DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed)
            {
                return;
            }
            try
            {
                action();
            }
            catch (Exception exception)
            {
                _exceptionHandler.Handle(exception);
            }
        });
    }

    private void Item_StateChanged(object? sender, EventArgs e)
    {
        foreach ((NavigationViewItem item, PluginMenuItem model) in _items)
        {
            if (ReferenceEquals(model, sender))
            {
                Render(item, model.State);
                return;
            }
        }
    }

    private static void Render(NavigationViewItem item, PluginMenuItemState state)
    {
        item.Content = state.Label;
        item.IsEnabled = state.IsEnabled;
        item.Icon = string.IsNullOrEmpty(state.IconGlyph) ? null : new FontIcon
        {
            Glyph = state.IconGlyph,
            FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"]
        };
        AutomationProperties.SetName(item, state.AccessibleName);
        ToolTipService.SetToolTip(item, state.ToolTipText);

        string? styleKey = state.Indicator.Kind switch
        {
            MainMenuIndicatorKind.Informational => "InformationalIconInfoBadgeStyle",
            MainMenuIndicatorKind.Success => "SuccessIconInfoBadgeStyle",
            MainMenuIndicatorKind.Warning => "CautionIconInfoBadgeStyle",
            MainMenuIndicatorKind.Error => "CriticalIconInfoBadgeStyle",
            MainMenuIndicatorKind.Progress => "InformationalIconInfoBadgeStyle",
            _ => null
        };
        if (styleKey is null)
        {
            item.InfoBadge = null;
            return;
        }

        InfoBadge badge = new() { Style = (Style)Application.Current.Resources[styleKey] };
        if (state.Indicator.Kind == MainMenuIndicatorKind.Progress)
        {
            badge.IconSource = new SymbolIconSource { Symbol = Symbol.Sync };
        }
        AutomationProperties.SetName(badge, state.Indicator.AccessibleText);
        item.InfoBadge = badge;
    }
}
