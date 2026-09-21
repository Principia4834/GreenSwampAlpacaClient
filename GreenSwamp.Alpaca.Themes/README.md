# GreenSwamp.Alpaca.Themes

Fluent-based Avalonia day/night theme package for `Avalonia 12.1.2`.

## Purpose

This package provides:
- semantic day/night resource dictionaries
- reusable control-family styling layered on top of `FluentTheme`
- support for app-wide or subtree-scoped day/night switching via `RequestedThemeVariant` and `ThemeVariantScope`

This package intentionally does **not** replace the full Fluent control template set with package-owned `ControlTheme`s.
It follows Avalonia guidance by:
- keeping `FluentTheme` as the structural base
- using semantic resources plus selector-based styling for broad reusable coverage
- using targeted template-part selectors only where necessary

## Consumption

Merge package resources in `Application.Resources`:

```xml
<Application.Resources>
	<ResourceDictionary>
		<ResourceDictionary.MergedDictionaries>
			<ResourceInclude Source="avares://GreenSwamp.Alpaca.Themes/Resources/Package/ThemeResources.axaml" />
		</ResourceDictionary.MergedDictionaries>
	</ResourceDictionary>
</Application.Resources>
```

Merge package styles after `FluentTheme` in `Application.Styles`:

```xml
<Application.Styles>
	<FluentTheme />
	<StyleInclude Source="avares://GreenSwamp.Alpaca.Themes/Resources/Package/ThemeStyles.axaml" />
</Application.Styles>
```

## Theme switching

Set the application or subtree variant with Avalonia-native APIs:

```xml
<ThemeVariantScope RequestedThemeVariant="Dark">
	<!-- themed subtree -->
</ThemeVariantScope>
```

Or at app/window level:
- `RequestedThemeVariant="Light"`
- `RequestedThemeVariant="Dark"`
- `RequestedThemeVariant="Default"`

## Public semantic contract

Consumer code and consumer-local styles should bind to `Theme.*` semantic resources rather than primitive color tokens.

Examples:
- `Theme.WindowBackgroundBrush`
- `Theme.ForegroundBrush`
- `Theme.BorderBrush`
- `Theme.AccentBrush`
- `Theme.InputBackgroundBrush`
- `Theme.PopupBackgroundBrush`
- `Theme.SelectionIndicatorBrush`

## Coverage

Current package coverage includes reusable styling for:
- windows and text
- buttons
- text input families
- combo-box items and popup surfaces
- tabs
- sliders
- progress bars
- list/tree items
- menu/context menu surfaces
- scroll bars
- checkbox/radio/toggle indicators
- expanders
- reusable surface/card patterns

## Opt-in classes

Some styles remain intentionally opt-in because they represent package variants or demo-facing composition patterns rather than universal defaults.

Examples:
- `theme-accent`
- `theme-secondary`
- `theme-card`
- `theme-tabs`
- `theme-slider`
- `theme-input`

## Design rules

For long-term maintainability:
- prefer `Theme.*` semantic resources over direct primitive token references
- prefer selector-based augmentation over template replacement
- introduce `ControlTheme` only when the package must own a full control template
- keep app-specific composition styles outside this package

## Known boundary

This package is a reusable Fluent-based theme layer, not a complete replacement for every Avalonia control template.
Where Avalonia/Fluent internals require template-part styling, those selectors are centralized in the package and kept as narrow as possible.
