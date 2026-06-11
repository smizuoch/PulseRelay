using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace PulseRelay.Desktop.Localization;

/// <summary>
/// Usage: <c>Text="{loc:Loc Action_Start}"</c>. Produces a one-way binding to the
/// <see cref="Loc"/> indexer; a reflection binding is used deliberately so the indexer
/// path works regardless of the compiled-bindings default.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key) => Key = key;

    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new ReflectionBindingExtension($"[{Key}]")
        {
            Mode = BindingMode.OneWay,
            Source = Loc.Instance,
        }.ProvideValue(serviceProvider);
}
